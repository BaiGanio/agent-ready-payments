using System.Buffers.Text;
using System.Security.Cryptography;

namespace Payments.Mcp;

/// <summary>
/// The exact refund a human approved: which payment, how much, in what currency.
/// A record, so two sets of arguments compare as values and 80.00m equals 80m.
/// </summary>
internal sealed record RefundArgs(Guid PaymentId, decimal Amount, string Currency);

/// <summary>
/// What a claim on a token gave you. A closed set, the same idea as lab 03's
/// <c>Claim</c>: the caller must handle every case, and a new case breaks the
/// build instead of slipping through a default branch.
/// </summary>
internal abstract record Redeem
{
    /// <summary>This caller may now do the refund. Exactly one caller ever gets this.</summary>
    public sealed record Won : Redeem;

    /// <summary>No such token. It was never issued, or the server restarted.</summary>
    public sealed record Unknown : Redeem;

    /// <summary>The token was issued, but the human took too long.</summary>
    public sealed record Expired : Redeem;

    /// <summary>The arguments are not the ones the human approved. The token is untouched.</summary>
    public sealed record Mismatch : Redeem;

    /// <summary>Another caller holds this token right now.</summary>
    public sealed record InProgress : Redeem;

    /// <summary>The refund already ran. This is the answer it gave, word for word.</summary>
    public sealed record AlreadyDone(string OutcomeJson) : Redeem;
}

/// <summary>
/// The second gate on a refund. A client should ask the human before a destructive
/// call, but a client that skips its own question still cannot refund: it needs a
/// token this server issued, for exactly these arguments, within five minutes.
/// </summary>
/// <remarks>
/// Step 14 fixes the shape only. Every method throws until step 15 fills it in.
/// Tokens live in this process's memory (decided 2026-10-21, `plan/PLAN.md` §8),
/// so a restart forgets them and every claim is Unknown. That is the safe way round.
/// </remarks>
internal sealed class ConfirmationTokens(TimeProvider clock)
{
    /// <summary>How long a human has to say yes.</summary>
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>The clock. A FakeTimeProvider in the tests, so "5 minutes later" is one line.</summary>
    private TimeProvider Clock { get; } = clock;

    /// <summary>
    /// One lock for every read and every write. A ConcurrentDictionary would not be
    /// enough: "read the entry, check it is Pending, write Executing" is three steps,
    /// and two callers can both pass the check. Inside this lock it is one step.
    /// </summary>
    private readonly Lock _gate = new();

    private readonly Dictionary<string, Entry> _tokens = new(StringComparer.Ordinal);

    /// <summary>Issues a token for exactly these arguments, and says when it dies.</summary>
    internal (string Token, DateTimeOffset ExpiresAt) Propose(RefundArgs args)
    {
        // RandomNumberGenerator, never Random and never a Guid. The token is a
        // capability: whoever holds it may move money, so it must not be guessable.
        // 16 bytes in base64url is 22 characters.
        var token = "rf_" + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));

        var expiresAt = Clock.GetUtcNow() + Lifetime;

        lock (_gate) _tokens[token] = new Entry(args, expiresAt);

        return (token, expiresAt);
    }

    /// <summary>
    /// Tries to take the token for these arguments. The check and the change are one
    /// step, so of many callers at once exactly one gets <see cref="Redeem.Won"/>.
    /// </summary>
    internal Redeem Claim(string token, RefundArgs args)
    {
        lock (_gate)
        {
            if (!_tokens.TryGetValue(token, out var entry)) return new Redeem.Unknown();

            // Done comes before Expired on purpose. A refund that already ran must
            // replay its answer for ever, not turn into "expired" five minutes later.
            if (entry.Phase is Phase.Done) return new Redeem.AlreadyDone(entry.OutcomeJson!);

            if (Clock.GetUtcNow() >= entry.ExpiresAt) return new Redeem.Expired();

            // A mismatch leaves the entry alone, so the approved refund can still be sent.
            if (entry.Args != args) return new Redeem.Mismatch();

            if (entry.Phase is Phase.Executing) return new Redeem.InProgress();

            entry.Phase = Phase.Executing;

            return new Redeem.Won();
        }
    }

    /// <summary>Stores the final answer. Every later claim replays it.</summary>
    internal void Complete(string token, string outcomeJson)
    {
        lock (_gate)
        {
            if (_tokens.TryGetValue(token, out var entry))
            {
                entry.Phase = Phase.Done;
                entry.OutcomeJson = outcomeJson;
            }
        }
    }

    /// <summary>Puts the token back, for a refund whose outcome is in doubt.</summary>
    internal void Release(string token)
    {
        lock (_gate)
        {
            // Only from Executing. Releasing a Done token would let the refund run twice.
            if (_tokens.TryGetValue(token, out var entry) && entry.Phase is Phase.Executing)
            {
                entry.Phase = Phase.Pending;
            }
        }
    }


     /// <summary>The three lives of a token. Expired is the clock, not a phase.</summary>
    private enum Phase { Pending, Executing, Done, }

    /// <summary>One token's entry. Only touched inside the lock.</summary>
    private sealed class Entry(RefundArgs args, DateTimeOffset expiresAt)
    {
        internal RefundArgs Args { get; } = args;
        internal DateTimeOffset ExpiresAt { get; } = expiresAt;
        internal Phase Phase { get; set; } = Phase.Pending;
        internal string? OutcomeJson { get; set; }
    }
}
