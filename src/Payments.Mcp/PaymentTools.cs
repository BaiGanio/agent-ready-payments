using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Payments.Mcp;

/// <summary>
/// The three tools an agent may call. This step fixes the shape only: the names,
/// the arguments, the annotations and the descriptions. Every body answers
/// tool_failed until the later steps fill them in.
/// </summary>
[McpServerToolType]
public sealed class PaymentTools
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
    public CallToolResult GetPayment(
        [Description("The payment id, a UUID such as 0199c3f0-1a2b-7c3d-8e4f-5a6b7c8d9e0f.")]
        [RegularExpression(UuidPattern)]
        string payment_id)
        {
            var id = ToolArguments.PaymentId(payment_id);
            if (id.Failed) return id.Error!;

            return NotBuiltYet();
        }

    [McpServerTool(Name = "list_payments", ReadOnly = true, Idempotent = true)]
    [Description(ListPaymentsDescription)]
    public CallToolResult ListPayments(
        [Description("Keep only payments in this status. Leave it out for every status. Case matters.")]
        [AllowedValues(
            "Created", "Authorised", "Declined", "Voided",
            "PartiallyCaptured", "Captured", "PartiallyRefunded", "Refunded")]
        string? status = null,
        [Description("How many payments to return, 1 to 20. The default is 20.")]
        [Range(1, 20)]
        int limit = 20)
        {
            var wanted = ToolArguments.Status(status);
            if (wanted.Failed) return wanted.Error!;

            var take = ToolArguments.Limit(limit);
            if (take.Failed) return take.Error!;

            return NotBuiltYet();
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
}