namespace Payments.Domain;

public readonly record struct Money
{
    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }
    public string Currency { get; }

    public bool IsZero => Amount == 0m;

    public static Money Of(decimal amount, string currency)
    {
        if(amount < 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "An amount cannot be negative.");

        if(currency is not {Length: 3} || !currency.All(char.IsAsciiLetterUpper))
            throw new ArgumentException("A currency must be exactly 3 upper-case letters", nameof(currency));

        return new Money(amount, currency);
    }
    public static Money Zero(string currency) => Of(0m, currency);

    public static Money operator +(Money a, Money b)
    {
        SameCurrency(a, b);
        return new Money(a.Amount + b.Amount, a.Currency);
    }
    public static Money operator -(Money a, Money b)
    {
        SameCurrency(a, b);
        return Of(a.Amount - b.Amount, a.Currency);
    }

    public static bool operator >(Money a, Money b)
    {
        SameCurrency(a, b);
        return a.Amount > b.Amount;
    }

    public static bool operator <(Money a, Money b)
    {
        SameCurrency(a, b);
        return a.Amount < b.Amount;
    }
    public static bool operator >=(Money a, Money b)
    {
        SameCurrency(a, b);
        return a.Amount >= b.Amount;
    }

    public static bool operator <=(Money a, Money b)
    {
        SameCurrency(a, b);
        return a.Amount <= b.Amount;
    }

    private static void SameCurrency(Money a, Money b)
    {
        if(a.Currency != b.Currency)
            throw new CurrencyMismatchException($"Cannot mix {a.Currency} and {b.Currency}");
    }
}