namespace Payments.Domain.Tests;

/// <summary>
/// Authorisation asks the issuer to hold the money. No money moves yet.
/// </summary>
public class AuthoriseTests
{
    [Fact]
    public void A_new_payment_is_created_with_nothing_captured_and_nothing_refunded()
    {
        var payment = TestData.NewPayment();

        Assert.Equal(PaymentStatus.Created, payment.Status);
        Assert.Equal(TestData.Eur100, payment.Amount);
        Assert.Equal(Money.Zero(TestData.Eur), payment.CapturedTotal);
        Assert.Equal(Money.Zero(TestData.Eur), payment.RefundedTotal);
        Assert.Empty(payment.Operations);
    }

    [Fact]
    public void Create_refuses_an_empty_id()
    {
        Assert.Throws<ArgumentException>(() => Payment.Create("  ", TestData.Eur100, new FixedClock()));
    }

    [Fact]
    public void Create_refuses_a_zero_amount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Payment.Create("pay_1", Money.Zero(TestData.Eur), new FixedClock()));
    }

    [Fact]
    public void Authorise_moves_a_created_payment_to_authorised()
    {
        var payment = TestData.NewPayment();

        var result = payment.Authorise();

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Authorised, payment.Status);
    }

    [Fact]
    public void Authorise_returns_the_same_payment()
    {
        var payment = TestData.NewPayment();

        var result = payment.Authorise();

        Assert.Same(payment, result.Value);
    }

    [Fact]
    public void Authorise_records_one_operation_for_the_full_amount()
    {
        var payment = TestData.NewPayment();

        payment.Authorise();

        var operation = Assert.Single(payment.Operations);
        Assert.Equal(PaymentAction.Authorise, operation.Kind);
        Assert.Equal(TestData.Eur100, operation.Amount);
    }

    [Fact]
    public void Authorising_twice_is_refused()
    {
        var payment = TestData.Authorised();

        var result = payment.Authorise();

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrorCode.IllegalTransition, result.Error.Code);
    }

    [Fact]
    public void Decline_moves_a_created_payment_to_declined()
    {
        var payment = TestData.NewPayment();

        var result = payment.Decline("insufficient funds");

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Declined, payment.Status);
    }

    [Fact]
    public void Decline_keeps_the_reason()
    {
        var payment = TestData.NewPayment();

        payment.Decline("insufficient funds");

        Assert.Equal("insufficient funds", payment.DeclineReason);
    }

    [Fact]
    public void Decline_refuses_an_empty_reason()
    {
        var payment = TestData.NewPayment();

        Assert.Throws<ArgumentException>(() => payment.Decline("  "));
    }

    [Fact]
    public void An_error_message_says_what_was_refused()
    {
        var payment = TestData.Captured();

        var result = payment.Void();

        Assert.True(result.IsFailure);
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
    }
}
