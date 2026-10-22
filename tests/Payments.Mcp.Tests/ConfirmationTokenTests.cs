using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Payments.Mcp.Tests;

/// <summary>
/// The token store on its own, with no MCP server and no HTTP. It is a small
/// concurrent state machine: Pending to Executing to Done, plus Expired.
/// Lab 03 taught that the race lives in the claim, so Two_claims_at_once_one_wins
/// is the test that matters most here.
/// </summary>
public sealed class ConfirmationTokenTests
{
    private static readonly Guid PaymentId = Guid.Parse("0199c3f0-1a2b-7c3d-8e4f-5a6b7c8d9e0f");

    /// <summary>The refund the human approved. Change one part and it is a different refund.</summary>
    private static RefundArgs Approved(decimal amount = 80.00m, string currency = "EUR")
        => new(PaymentId, amount, currency);

    /// <summary>A fresh store on a stopped clock, so the test moves time by hand.</summary>
    private static (ConfirmationTokens Store, FakeTimeProvider Clock) NewStore()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero));

        return (new ConfirmationTokens(clock), clock);
    }

    [Fact]
    public void A_proposal_gets_an_unguessable_token()
    {
        var (store, _) = NewStore();

        var (token, _) = store.Propose(Approved());

        Assert.StartsWith("rf_", token, StringComparison.Ordinal);

        // 16 random bytes in base64url is 22 characters. Fewer means it is guessable,
        // and a guessable capability is no gate at all.
        Assert.True(
            token.Length - 3 >= 22,
            $"'{token}' has only {token.Length - 3} characters after rf_.");

        var seen = new HashSet<string>(StringComparer.Ordinal) { token };

        for (var i = 0; i < 1_000; i++)
        {
            var (next, _) = store.Propose(Approved());

            Assert.True(seen.Add(next), $"two proposals gave the same token: '{next}'.");
        }

        Assert.Equal(1_001, seen.Count);
    }

    [Fact]
    public void The_same_arguments_redeem()
    {
        var (store, _) = NewStore();

        var (token, _) = store.Propose(Approved());

        Assert.IsType<Redeem.Won>(store.Claim(token, Approved()));
    }

    [Fact]
    public void Other_arguments_are_a_mismatch_and_keep_the_token()
    {
        var (store, _) = NewStore();

        var (token, _) = store.Propose(Approved());

        Assert.IsType<Redeem.Mismatch>(store.Claim(token, Approved(amount: 79.99m)));

        // A mismatch must not burn the token. The human approved 80.00, so the model
        // that sent 79.99 by mistake can still send the refund that was approved.
        Assert.IsType<Redeem.Won>(store.Claim(token, Approved()));
    }

    [Fact]
    public void A_token_lives_five_minutes()
    {
        var (store, clock) = NewStore();

        var (token, expiresAt) = store.Propose(Approved());

        Assert.Equal(clock.GetUtcNow() + TimeSpan.FromMinutes(5), expiresAt);

        clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));

        Assert.IsType<Redeem.Won>(store.Claim(token, Approved()));

        // A fresh token, because the first one is being used now. The boundary is
        // closed: at exactly five minutes the token is already gone.
        var (late, _) = store.Propose(Approved());

        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.IsType<Redeem.Expired>(store.Claim(late, Approved()));
    }

    [Fact]
    public void An_unknown_token_is_unknown()
    {
        var (store, _) = NewStore();

        // A token this server never issued, or one from before a restart.
        Assert.IsType<Redeem.Unknown>(store.Claim("rf_doesnotexist", Approved()));
    }

    [Fact]
    public void A_done_token_returns_its_stored_outcome()
    {
        var (store, _) = NewStore();

        var (token, _) = store.Propose(Approved());

        Assert.IsType<Redeem.Won>(store.Claim(token, Approved()));

        const string outcome = """{"result":"refunded","refund_id":"0199c3f0-aaaa-7000-8000-000000000001"}""";

        store.Complete(token, outcome);

        // The stored answer comes back word for word. A second refund must never happen,
        // and the model must see the same answer it would have seen the first time.
        var again = Assert.IsType<Redeem.AlreadyDone>(store.Claim(token, Approved()));

        Assert.Equal(outcome, again.OutcomeJson);
    }

    [Fact]
    public async Task Two_claims_at_once_one_wins()
    {
        var (store, _) = NewStore();

        var (token, _) = store.Propose(Approved());

        // Every racer waits on one gate, so all 20 claims land together. A
        // ConcurrentDictionary alone does not make this safe: "read, check Pending,
        // write Executing" is lab 03's check-then-act race, in memory.
        var gate = new TaskCompletionSource();

        var racers = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(async () =>
            {
                await gate.Task;

                return store.Claim(token, Approved());
            }))
            .ToArray();

        gate.SetResult();

        var results = await Task.WhenAll(racers);

        Assert.Equal(1, results.Count(result => result is Redeem.Won));
        Assert.Equal(19, results.Count(result => result is Redeem.InProgress));
    }
}
