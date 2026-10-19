namespace Payments.Domain;

public sealed class CurrencyMismatchException(string message) : Exception(message);
