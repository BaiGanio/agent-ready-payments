namespace Payments.Domain;

public sealed record PaymentError(PaymentErrorCode Code, string Message);
