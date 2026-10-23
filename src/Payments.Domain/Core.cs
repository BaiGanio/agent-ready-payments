namespace Payments.Domain;

public sealed class CurrencyMismatchException(string message) : Exception(message);
public sealed record PaymentError(PaymentErrorCode Code, string Message);
public sealed record PaymentOperation(PaymentAction Kind, Money Amount, DateTimeOffset At);


public enum PaymentAction { Authorise, Decline, Capture, Void, Refund }
public enum PaymentErrorCode { IllegalTransition, AmountNotPositive, AmountExceedsAuthorised, AmountExceedsCaptured, CurrencyMismatch }
public enum PaymentStatus { Created, Authorised, Declined,  Voided, PartiallyCaptured, Captured, PartiallyRefunded, Refunded }
