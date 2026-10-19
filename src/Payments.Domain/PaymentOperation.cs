namespace Payments.Domain;

public sealed record PaymentOperation(PaymentAction Kind, Money Amount, DateTimeOffset At);
