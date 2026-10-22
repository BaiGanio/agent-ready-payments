using System.ComponentModel.DataAnnotations;
using Payments.Domain;

namespace Payments.Api;
public record CreatePaymentRequest(
    [Required] Guid MerchantId,
    [Range(0.01, 1_000_000)] decimal Amount,
    [Required, RegularExpression("^[A-Z]{3}$")] string Currency);

public record AmountRequest(
    [Range(0.01, 1_000_000)] decimal Amount,
    [Required, RegularExpression("^[A-Z]{3}$")] string Currency);

public record PaymentDto(
    Guid Id,
    Guid MerchantId,
    string Status,
    string Currency,
    decimal AuthorisedAmount,
    decimal CapturedAmount,
    decimal RefundedAmount,
    decimal CapturableRemaining,
    decimal RefundableRemaining,
    IReadOnlyCollection<string> AllowedActions,
    string? DeclineReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static PaymentDto From(Payment p, Guid merchantId, DateTimeOffset updatedAt) =>
        new(p.Id,
            merchantId,
            p.Status.ToString(),
            p.Amount.Currency,
            p.Amount.Amount,
            p.CapturedTotal.Amount,
            p.RefundedTotal.Amount,
            p.CapturableRemaining.Amount,
            p.RefundableRemaining.Amount,
            [.. p.AllowedActions.Select(a => a.ToString())],
            p.DeclineReason,
            p.CreatedAt,
            updatedAt);
}

public record PaymentSummary(Guid Id, string Status, string Currency, decimal AuthorisedAmount, decimal CapturedAmount, decimal RefundedAmount, DateTimeOffset CreatedAt);

public record PaymentList(IReadOnlyList<PaymentSummary> Items, bool Truncated);