using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Payments.Mcp;

/// <summary>
/// The three tools an agent may call. The two read tools are built; refund_payment
/// still answers tool_failed until the token store lands.
/// </summary>
/// <remarks>
/// internal, not public: it takes the internal PaymentsApi, and a public type may not
/// take a less accessible one in its constructor (CS0051). The tests see it through
/// InternalsVisibleTo.
/// </remarks>
[McpServerToolType]
internal sealed class PaymentTools(PaymentsApi api, ConfirmationTokens tokens)
{

    /// <summary>A UUID in the plain 8-4-4-4-12 form. The SDK copies this into the schema as "pattern".</summary>
    internal const string UuidPattern =
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";

    internal const string GetPaymentDescription =
        "Read one payment by its exact id. Returns the status, the currency and the " +
        "authorised, captured and refunded amounts. You must already have the payment id, " +
        "a UUID. If you only have a merchant, a date or a customer, call list_payments first. " +
        "An id that does not exist returns result: not_found, which is an answer, not a failure. " +
        "This tool only reads; it never changes a payment.";

    internal const string ListPaymentsDescription =
        "List this merchant's payments, newest first, at most 20 at a time. The merchant is " +
        "fixed by the API key, so you cannot ask for another merchant's payments. Use status to " +
        "narrow the list. truncated: true means there are more payments than were returned; " +
        "narrow the list with status rather than asking for more. There is no paging: " +
        "there is no cursor, no page and no offset argument, so never invent one. " +
        "This tool only reads; it never changes a payment.";

    internal const string RefundPaymentDescription =
        "Refund money from a payment back to the customer. This takes two calls. " +
        "The first call refunds nothing: it returns a confirmation_token and the exact refund " +
        "it would make, for a human to approve. Only after a human has said yes, call it a " +
        "second time with the same payment_id, amount and currency plus that confirmation_token; " +
        "that second call moves the money. Give amount as a plain decimal string such as \"80.00\", " +
        "and currency as a three-letter upper-case code such as \"EUR\". " +
        "Never call this twice for the same refund without a new approval.";

    [McpServerTool(Name = "get_payment", ReadOnly = true, Idempotent = true)]
    [Description(GetPaymentDescription)]
    public async Task<CallToolResult> GetPayment(
        [Description("The payment id, a UUID such as 0199c3f0-1a2b-7c3d-8e4f-5a6b7c8d9e0f.")]
        [RegularExpression(UuidPattern)]
        string payment_id,
        CancellationToken cancellationToken = default)
        {
            var id = ToolArguments.PaymentId(payment_id);
            if (id.Failed) return id.Error!;

            var answer = await api.GetAsync(id.Value, cancellationToken);

            if (answer.Ok)
            {
                return Ok(new { result = "found", payment = answer.Value });
            }

            // A miss is a fact, not a failure. An agent that reads "not found" as an
            // error retries, and a retry loop on a miss burns the conversation.
            if (answer.Status == 404)
            {
                return Ok(new { result = "not_found", payment_id = id.Value.ToString("D") });
            }

            return Mapped(answer);
        }

    [McpServerTool(Name = "list_payments", ReadOnly = true, Idempotent = true)]
    [Description(ListPaymentsDescription)]
    public async Task<CallToolResult> ListPayments(
        [Description("Keep only payments in this status. Leave it out for every status. Case matters.")]
        [AllowedValues(
            "Created", "Authorised", "Declined", "Voided",
            "PartiallyCaptured", "Captured", "PartiallyRefunded", "Refunded")]
        string? status = null,
        [Description("How many payments to return, 1 to 20. The default is 20.")]
        [Range(1, 20)]
        int limit = 20,
        CancellationToken cancellationToken = default)
        {
            var wanted = ToolArguments.Status(status);
            if (wanted.Failed) return wanted.Error!;

            var take = ToolArguments.Limit(limit);
            if (take.Failed) return take.Error!;

            var answer = await api.ListAsync(wanted.Value, take.Value, cancellationToken);
            if (!answer.Ok) return Mapped(answer);

            var body = answer.Value;

            var truncated = body.TryGetProperty("truncated", out var more) &&
                            more.ValueKind is JsonValueKind.True;

            var items = body.TryGetProperty("items", out var rows)
                ? rows
                : JsonSerializer.SerializeToElement(Array.Empty<object>());

            return Ok(new
            {
                result = "ok",
                items,
                truncated,

                // There is no paging, so the only way to see less is to narrow.
                hint = truncated
                    ? "There are more payments than these. There is no paging and no cursor: " +
                      "narrow the list with status instead of asking for more."
                    : null,
            });
        }

    [McpServerTool(Name = "refund_payment", Destructive = true, ReadOnly = false)]
    [Description(RefundPaymentDescription)]
     public async Task<CallToolResult> RefundPayment(
        [Description("The payment id, a UUID such as 0199c3f0-1a2b-7c3d-8e4f-5a6b7c8d9e0f.")]
        [RegularExpression(UuidPattern)]
        string payment_id,
        [Description("The amount to refund, as a plain decimal string such as \"80.00\". " +
                     "Use a dot, never a comma, and at most two decimal places.")]
        string amount,
        [Description("The currency, as a three-letter upper-case code such as \"EUR\".")]
        string currency,
        [Description("Leave this out on the first call. On the second call, send back the exact " +
                     "confirmation_token the first call returned, after a human approved it.")]
        string? confirmation_token = null,
        CancellationToken cancellationToken = default)
        {
            // Both phases parse the same three arguments. Phase 2 does not trust phase 1
            // for them: the token is the only thing it carries over.
            var id = ToolArguments.PaymentId(payment_id);
            if (id.Failed) return id.Error!;

            var money = ToolArguments.Amount(amount);
            if (money.Failed) return money.Error!;

            var code = ToolArguments.Currency(currency);
            if (code.Failed) return code.Error!;

            var args = new RefundArgs(id.Value, money.Value, code.Value);

            return string.IsNullOrEmpty(confirmation_token)
                ? await ProposeAsync(args, cancellationToken)
                : await ConfirmAsync(confirmation_token, args, cancellationToken);
        }

     /// <summary>
    /// Phase 1. It reads the payment so the proposal is concrete, refuses early what
    /// the API would refuse anyway, and moves no money. A token for an impossible
    /// refund is worse than no token: a human approves it and nobody checked.
    /// </summary>
    private async Task<CallToolResult> ProposeAsync(
        RefundArgs args, CancellationToken cancellationToken)
    {
        var answer = await api.GetAsync(args.PaymentId, cancellationToken);

        if (!answer.Ok)
        {
            // For a read, a miss is a fact. For a refund it is a wrong id, so the
            // agent must stop instead of proposing a refund it cannot see.
            return answer.Status == 404
                ? Failed(
                    "payment_not_found",
                    "There is no payment with that id for this key.",
                    "Check the id with list_payments. Do not refund a payment you cannot read.")
                : Mapped(answer);
        }

        var payment = answer.Value;

        if (!payment.TryGetProperty("currency", out var paid) ||
            !payment.TryGetProperty("refundableRemaining", out var left) ||
            paid.GetString() is not { } paidCurrency ||
            !left.TryGetDecimal(out var refundable))
        {
            return Failed(
                "tool_failed",
                "The payments API answered in a shape this tool does not understand.",
                "Tell the person and stop. Trying again will not change the shape.");
        }

        // 80 USD is not 80 EUR. A token that holds the wrong currency turns an
        // exchange rate into a loss, and the human read "80" and said yes.
        if (!string.Equals(paidCurrency, args.Currency, StringComparison.Ordinal))
        {
            return Failed(
                "currency_mismatch",
                $"That payment is in {paidCurrency}, not {args.Currency}.",
                $"Call this tool again with currency \"{paidCurrency}\".");
        }

        if (args.Amount > refundable)
        {
            return Failed(
                "amount_exceeds_captured",
                $"At most {Money(refundable)} {paidCurrency} is left to refund on that payment.",
                $"Call this tool again with an amount of {Money(refundable)} or less.");
        }

        var (token, expiresAt) = tokens.Propose(args);

        return Ok(new
        {
            result = "confirmation_required",
            proposal =
                $"Refund {Money(args.Amount)} {args.Currency} on payment " +
                $"{args.PaymentId:D}? Nothing has been refunded yet.",
            confirmation_token = token,
            expires_at = expiresAt.ToString("O", CultureInfo.InvariantCulture),
            next =
                "Show this proposal to a human and wait for their answer. Only after the " +
                "human says yes, call refund_payment again with the same payment_id, amount " +
                "and currency plus this confirmation_token. Never approve it yourself.",
        });
    }

     /// <summary>
    /// Phase 2. The token is the only thing it trusts from phase 1; the API checks the
    /// state, the amount and the scope again, because five minutes passed.
    /// </summary>
    private async Task<CallToolResult> ConfirmAsync(
        string token, RefundArgs args, CancellationToken cancellationToken)
    {
        switch (tokens.Claim(token, args))
        {
            case Redeem.Unknown:
                // A model that can write its own token has no gate at all.
                return Failed(
                    "token_unknown",
                    "That confirmation_token was not issued by this server, or the server restarted.",
                    "Call refund_payment without a token to make a new proposal, and have a " +
                    "human approve that one.");

            case Redeem.Expired:
                // A yes from an hour ago is not a yes now: the payment may have moved.
                return Failed(
                    "token_expired",
                    "That confirmation_token is older than five minutes.",
                    "Call refund_payment without a token to make a new proposal, and have a " +
                    "human approve that one.");

            case Redeem.Mismatch:
                // The human approved one exact refund. This is not it.
                return Failed(
                    "token_mismatch",
                    "These arguments are not the ones that token was issued for.",
                    "Send back exactly the payment_id, amount and currency of the proposal the " +
                    "human approved, or make a new proposal.");

            case Redeem.InProgress:
                // Two clicks, or two agents on one approval.
                return Failed(
                    "confirmation_in_progress",
                    "That refund is at the payments API right now.",
                    "Wait for the first call to answer. Do not start another refund.");

            case Redeem.AlreadyDone done:
                // The refund already ran. Replay its answer for ever, post nothing.
                return Replayed(done.OutcomeJson);

            case Redeem.Won:
                return await RefundAsync(token, args, cancellationToken);

            default:
                return Failed(
                    "tool_failed",
                    "This tool could not read the state of that confirmation.",
                    "Tell the person and stop.");
        }
    }

     /// <summary>
    /// The one call that moves money. The Idempotency-Key is derived from the token,
    /// so one approval is one key however often the network drops the answer.
    /// </summary>
    private async Task<CallToolResult> RefundAsync(
        string token, RefundArgs args, CancellationToken cancellationToken)
    {
        var answer = await api.RefundAsync(
            args.PaymentId, args.Amount, args.Currency, "mcp-" + token, cancellationToken);

        if (answer.Ok)
        {
            var refunded = Ok(new { result = "refunded", payment = answer.Value });

            tokens.Complete(token, TextOf(refunded));

            return refunded;
        }

        // A 4xx is the API's final answer: it decided, and it will decide the same way
        // again. Store it, so a repeat replays it instead of asking twice.
        if (answer.Status is >= 400 and < 500)
        {
            var refused = Refused(answer);

            tokens.Complete(token, TextOf(refused));

            return refused;
        }

        // 5xx, or no answer at all. The refund may or may not have happened, so the
        // tool does not guess: the token goes back, and the same call is safe again.
        tokens.Release(token);

        return Failed(
            "refund_in_doubt",
            "The payments API did not say whether that refund went through.",
            "Call refund_payment again with the same arguments and the same " +
            "confirmation_token. It carries the same idempotency key, so the API " +
            "refunds once at most, whatever happened the first time.");
    }

     /// <summary>The API refused the refund. Its own codes come through, its words do not.</summary>
    private static CallToolResult Refused<T>(ApiResult<T> answer) => answer.Code switch
    {
        "amount_exceeds_captured" => Failed(
            "amount_exceeds_captured",
            "The payments API says that is more than is left to refund.",
            "Read the payment again with get_payment, then make a new proposal for an " +
            "amount a human approves."),
        "currency_mismatch" => Failed(
            "currency_mismatch",
            "The payments API says that is not the payment's currency.",
            "Read the payment again with get_payment, then make a new proposal."),
        _ => Mapped(answer),
    };

    /// <summary>
    /// The stored outcome, with replayed: true added, so the model can tell a replay
    /// from a fresh refund and does not count it twice.
    /// </summary>
    private static CallToolResult Replayed(string outcomeJson)
    {
        var payload = JsonNode.Parse(outcomeJson)!.AsObject();

        payload["replayed"] = true;

        return new CallToolResult
        {
            // An outcome with an error is still an error when it is replayed.
            IsError = payload.ContainsKey("error"),
            Content = [new TextContentBlock { Text = payload.ToJsonString() }],
        };
    }

    /// <summary>The JSON text of a result, which is what the token store keeps.</summary>
    private static string TextOf(CallToolResult result)
        => result.Content.OfType<TextContentBlock>().Single().Text;

    /// <summary>Money for a human to read: always two decimal places, always a dot.</summary>
    private static string Money(decimal amount)
        => amount.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>The one error shape every tool returns: error, message, hint.</summary>
    internal static CallToolResult Failed(string error, string message, string? hint = null)
    {
        var payload = JsonSerializer.Serialize(new { error, message, hint });

        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = payload }],
        };
    }

    /// <summary>One answer the model can read: a single JSON text block, not an error.</summary>
    internal static CallToolResult Ok(object payload)
        => new()
        {
            IsError = false,
            Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload) }],
        };

    /// <summary>
    /// The API said no. The API's own words never come through: a body may echo the
    /// API key or another merchant's data, and the model must see neither.
    /// </summary>
    private static CallToolResult Mapped<T>(ApiResult<T> answer) => answer.Status switch
    {
        403 => Failed(
            "insufficient_scope",
            "This API key is not allowed to do that.",
            "Trying again will not help. Only a human can widen the key's scopes. " +
            "Tell the person what you were trying to do, and stop."),
        _ => Failed(
            "tool_failed",
            "The payments API did not answer that call.",
            "Try once more. If it fails again, tell the person and stop."),
    };
}