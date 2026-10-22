using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
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
internal sealed class PaymentTools(PaymentsApi api)
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
    public CallToolResult RefundPayment(
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
        string? confirmation_token = null)
        {
            var id = ToolArguments.PaymentId(payment_id);
            if (id.Failed) return id.Error!;

            var money = ToolArguments.Amount(amount);
            if (money.Failed) return money.Error!;

            var code = ToolArguments.Currency(currency);
            if (code.Failed) return code.Error!;

            return NotBuiltYet();
        }

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

    private static CallToolResult NotBuiltYet()
        => Failed(
            "tool_failed",
            "This tool is not built yet.",
            "The shape is fixed in step 9; the body arrives in a later step.");

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