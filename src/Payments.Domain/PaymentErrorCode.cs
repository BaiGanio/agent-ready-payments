namespace Payments.Domain;

public enum PaymentErrorCode { IllegalTransition, AmountNotPositive, AmountExceedsAuthorised, AmountExceedsCaptured, CurrencyMismatch }
