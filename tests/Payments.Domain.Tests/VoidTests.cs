namespace Payments.Domain.Tests;

/// <summary>
/// Void lets the hold go before any money moved. After a capture, only a refund is left.
/// This is the pair interviewers ask about most.
/// </summary>
public class VoidTests
{
    [Fact]
    public void Voiding_an_authorised_payment_moves_to_voided()
    {
        var payment = TestData.Authorised();

        var result = payment.Void();

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Voided, payment.Status);
    }

    [Fact]
    public void Void_records_one_operation_for_the_authorised_amount()
    {
        var payment = TestData.Authorised();

        payment.Void();

        var voidOperation = Assert.Single(payment.Operations, o => o.Kind == PaymentAction.Void);
        Assert.Equal(TestData.Eur100, voidOperation.Amount);
    }

    [Fact]
    public void Voiding_a_captured_payment_is_refused()
    {
        var payment = TestData.Captured();

        var result = payment.Void();

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrorCode.IllegalTransition, result.Error.Code);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
    }

    [Fact]
    public void Voiding_a_partially_captured_payment_is_refused()
    {
        var payment = TestData.PartiallyCaptured();

        var result = payment.Void();

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrorCode.IllegalTransition, result.Error.Code);
    }

    [Fact]
    public void Voiding_twice_is_refused()
    {
        var payment = TestData.Voided();

        var result = payment.Void();

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrorCode.IllegalTransition, result.Error.Code);
    }

    [Fact]
    public void A_voided_payment_cannot_be_refunded()
    {
        var payment = TestData.Voided();

        var result = payment.Refund(TestData.InEur(10m));

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrorCode.IllegalTransition, result.Error.Code);
    }
}
