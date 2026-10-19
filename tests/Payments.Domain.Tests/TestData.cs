namespace Payments.Domain.Tests;

/// <summary>Short helpers so every test reads as one sentence.</summary>
public static class TestData
{
    public const string Eur = "EUR";
    public const string Usd = "USD";

    public static Money Eur100 => Money.Of(100m, Eur);

    public static Money InEur(decimal amount) => Money.Of(amount, Eur);

    /// <summary>A brand new payment of EUR 100, status <c>Created</c>.</summary>
    public static Payment NewPayment(TimeProvider? clock = null) =>
        Payment.Create(Guid.NewGuid(), Eur100, clock ?? new FixedClock());

    /// <summary>An authorised payment of EUR 100.</summary>
    public static Payment Authorised(TimeProvider? clock = null)
    {
        var payment = NewPayment(clock);
        payment.Authorise();
        return payment;
    }

    /// <summary>An authorised payment of EUR 100, fully captured.</summary>
    public static Payment Captured(TimeProvider? clock = null)
    {
        var payment = Authorised(clock);
        payment.Capture(Eur100);
        return payment;
    }

    /// <summary>An authorised payment of EUR 100 with EUR 40 captured.</summary>
    public static Payment PartiallyCaptured(TimeProvider? clock = null)
    {
        var payment = Authorised(clock);
        payment.Capture(InEur(40m));
        return payment;
    }

    /// <summary>EUR 100 captured, EUR 60 refunded.</summary>
    public static Payment PartiallyRefunded(TimeProvider? clock = null)
    {
        var payment = Captured(clock);
        payment.Refund(InEur(60m));
        return payment;
    }

    /// <summary>EUR 100 captured and fully refunded.</summary>
    public static Payment Refunded(TimeProvider? clock = null)
    {
        var payment = Captured(clock);
        payment.Refund(Eur100);
        return payment;
    }

    public static Payment Voided(TimeProvider? clock = null)
    {
        var payment = Authorised(clock);
        payment.Void();
        return payment;
    }

    public static Payment Declined(TimeProvider? clock = null)
    {
        var payment = NewPayment(clock);
        payment.Decline("insufficient funds");
        return payment;
    }

    /// <summary>Builds a payment that already sits in the wanted status.</summary>
    public static Payment InStatus(PaymentStatus status, TimeProvider? clock = null) => status switch
    {
        PaymentStatus.Created => NewPayment(clock),
        PaymentStatus.Authorised => Authorised(clock),
        PaymentStatus.Declined => Declined(clock),
        PaymentStatus.Voided => Voided(clock),
        PaymentStatus.PartiallyCaptured => PartiallyCaptured(clock),
        PaymentStatus.Captured => Captured(clock),
        PaymentStatus.PartiallyRefunded => PartiallyRefunded(clock),
        PaymentStatus.Refunded => Refunded(clock),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown status in the test matrix."),
    };
}
