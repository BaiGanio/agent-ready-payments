using System.Globalization;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;

namespace Payments.Mcp;

/// <summary>
/// One parsed argument: either a value, or the error result to return.
/// The same idea as the domain Result, kept local so the MCP server stays
/// free of a reference to the domain.
/// </summary>
internal readonly struct Parsed<T>
{
    private Parsed(T value, CallToolResult? error)
    {
        Value = value;
        Error = error;
    }

    internal T Value { get; }

    internal CallToolResult? Error { get; }

    internal bool Failed => Error is not null;

    internal static Parsed<T> Ok(T value) => new(value, null);

    internal static Parsed<T> Fail(CallToolResult error) => new(default!, error);
}

/// <summary>
/// Every value the model may send, checked here. The rule is refuse, never repair:
/// no Trim, no comma swapped for a dot, no rounding. A repaired amount is a guess,
/// and a guess on a refund is money sent to the wrong place.
/// </summary>
internal static partial class ToolArguments
{
    internal static readonly string[] Statuses =
    [
        "Created", "Authorised", "Declined", "Voided",
        "PartiallyCaptured", "Captured", "PartiallyRefunded", "Refunded",
    ];

    [GeneratedRegex(@"^\d{1,7}(\.\d{1,2})?$")]
    private static partial Regex AmountShape();

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyShape();

    internal static Parsed<Guid> PaymentId(string value)
        => Guid.TryParseExact(value, "D", out var id)
            ? Parsed<Guid>.Ok(id)
            : Parsed<Guid>.Fail(PaymentTools.Failed(
                "invalid_payment_id",
                $"payment_id must be a UUID. '{value}' is not one.",
                "A UUID looks like 0199c3f0-1a2b-7c3d-8e4f-5a6b7c8d9e0f. " +
                "Use list_payments to find the id you need."));

    internal static Parsed<decimal> Amount(string value)
    {
        if (!AmountShape().IsMatch(value))
        {
            return Parsed<decimal>.Fail(PaymentTools.Failed(
                "invalid_amount",
                $"amount must look like 80.00. '{value}' does not.",
                "Use digits and at most one dot, with at most two decimal places. " +
                "No comma, no currency sign, no spaces."));
        }

        var amount = decimal.Parse(value, CultureInfo.InvariantCulture);

        return amount > 0
            ? Parsed<decimal>.Ok(amount)
            : Parsed<decimal>.Fail(PaymentTools.Failed(
                "invalid_amount",
                $"amount must be more than zero. '{value}' is not.",
                "To cancel a payment that was never captured, use a void, not a refund."));
    }

    internal static Parsed<string> Currency(string value)
        => CurrencyShape().IsMatch(value)
            ? Parsed<string>.Ok(value)
            : Parsed<string>.Fail(PaymentTools.Failed(
                "invalid_currency",
                $"currency must be three upper-case letters, such as EUR. '{value}' is not.",
                "Send the ISO 4217 code, not a symbol and not a name."));

    internal static Parsed<string?> Status(string? value)
    {
        if (value is null)
        {
            return Parsed<string?>.Ok(null);
        }

        return Array.IndexOf(Statuses, value) >= 0
            ? Parsed<string?>.Ok(value)
            : Parsed<string?>.Fail(PaymentTools.Failed(
                "invalid_status",
                $"status must be one of {string.Join(", ", Statuses)}. '{value}' is not.",
                "Upper camel case, spelt exactly. 'captured' is not 'Captured'."));
    }

    internal static Parsed<int> Limit(int value)
        => value is >= 1 and <= 20
            ? Parsed<int>.Ok(value)
            : Parsed<int>.Fail(PaymentTools.Failed(
                "invalid_limit",
                $"limit must be between 1 and 20. {value} is not.",
                "Leave limit out for the default of 20, and narrow the list with status."));
}
