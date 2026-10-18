namespace Payments.Domain.Tests;

/// <summary>
/// A refund gives back money that already moved. The sum of refunds must never pass
/// the captured amount — that rule is the one that stops paying a customer twice.
/// </summary>
public class RefundTests
{
    [Fact]
    public void Refunding_the_whole_captured_amount_moves_to_refunded()
    {
        var payment = TestData.Captured();

        var result = payment.Refund(TestData.Eur100);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(TestData.Eur100, payment.RefundedTotal);
    }

    [Fact]
    public void Refunding_part_of_it_moves_to_partially_refunded()
    {
        var payment = TestData.Captured();

        var result = payment.Refund(TestData.InEur(60m));

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(TestData.InEur(60m), payment.RefundedTotal);
    }

    [Fact]
    public void Refunds_that_add_up_to_the_captured_amount_move_to_refunded()
    {
        var payment = TestData.PartiallyRefunded();   // EUR 100 captured, EUR 60 refunded

        var result = payment.Refund(TestData.InEur(40m));

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(TestData.Eur100, payment.RefundedTotal);
    }

    [Fact]
    public void Refunding_more_than_was_captured_is_refused()
    {
        var payment = TestData.PartiallyRefunded();   // EUR 100 captured, EUR 60 refunded

        var result = payment.Refund(TestData.InEur(40.01m));

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrorCode.AmountExceedsCaptured, result.Error.Code);
        Assert.Equal(TestData.InEur(60m), payment.RefundedTotal);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
    }

    [Fact]
    public void Refunding_one_cent_more_than_captured_is_refused()
    {
        var payment = TestData.Captured();

        var result = payment.Refund(TestData.InEur(100.01m));

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrorCode.AmountExceedsCaptured, result.Error.Code);
    }

    [Fact]
    public void A_refund_is_limited_by_what_was_captured_not_by_what_was_authorised()
    {
        var payment = TestData.PartiallyCaptured();   // EUR 100 authorised, EUR 40 captured

        var result = payment.Refund(TestData.InEur(40.01m));

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrorCode.AmountExceedsCaptured, result.Error.Code);
    }

    [Fact]
    public void Refunding_exactly_the_captured_part_of_a_partial_capture_is_allowed()
    {
        var payment = TestData.PartiallyCaptured();   // EUR 40 captured

        var result = payment.Refund(TestData.InEur(40m));

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    [Fact]
    public void Refunding_zero_is_refused()
    {
        var payment = TestData.Captured();

        var result = payment.Refund(Money.Zero(TestData.Eur));

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrorCode.AmountNotPositive, result.Error.Code);
    }

    [Fact]
    public void Refunding_another_currency_is_refused()
    {
        var payment = TestData.Captured();

        var result = payment.Refund(Money.Of(10m, TestData.Usd));

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrorCode.CurrencyMismatch, result.Error.Code);
    }

    [Fact]
    public void A_fully_refunded_payment_refuses_one_more_refund()
    {
        var payment = TestData.Refunded();

        var result = payment.Refund(TestData.InEur(0.01m));

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrorCode.IllegalTransition, result.Error.Code);
    }

    [Fact]
    public void Capture_after_a_refund_is_refused()
    {
        var payment = TestData.PartiallyRefunded();

        var result = payment.Capture(TestData.InEur(1m));

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrorCode.IllegalTransition, result.Error.Code);
    }

    [Fact]
    public void Refund_records_one_operation_per_refund()
    {
        var payment = TestData.Captured();

        payment.Refund(TestData.InEur(60m));
        payment.Refund(TestData.InEur(40m));

        var refunds = payment.Operations.Where(o => o.Kind == PaymentAction.Refund).ToList();
        Assert.Equal(2, refunds.Count);
        Assert.Equal(TestData.InEur(60m), refunds[0].Amount);
        Assert.Equal(TestData.InEur(40m), refunds[1].Amount);
    }
}
