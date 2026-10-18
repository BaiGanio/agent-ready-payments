namespace Payments.Domain.Tests;

/// <summary>
/// Capture claims the held money. The sum of captures must never pass the authorised amount.
/// The edges are the whole point: exactly equal, one cent over, zero.
/// </summary>
public class CaptureTests
{
    [Fact]
    public void Capturing_the_full_amount_moves_to_captured()
    {
        var payment = TestData.Authorised();

        var result = payment.Capture(TestData.Eur100);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(TestData.Eur100, payment.CapturedTotal);
    }

    [Fact]
    public void Capturing_part_of_the_amount_moves_to_partially_captured()
    {
        var payment = TestData.Authorised();

        var result = payment.Capture(TestData.InEur(40m));

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.PartiallyCaptured, payment.Status);
        Assert.Equal(TestData.InEur(40m), payment.CapturedTotal);
    }

    [Fact]
    public void Two_captures_that_add_up_to_the_authorised_amount_move_to_captured()
    {
        var payment = TestData.Authorised();

        payment.Capture(TestData.InEur(40m));
        var result = payment.Capture(TestData.InEur(60m));

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(TestData.Eur100, payment.CapturedTotal);
    }

    [Fact]
    public void Capturing_one_cent_more_than_authorised_is_refused()
    {
        var payment = TestData.Authorised();

        var result = payment.Capture(TestData.InEur(100.01m));

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrorCode.AmountExceedsAuthorised, result.Error.Code);
        Assert.Equal(PaymentStatus.Authorised, payment.Status);
        Assert.Equal(Money.Zero(TestData.Eur), payment.CapturedTotal);
    }

    [Fact]
    public void A_second_capture_that_passes_the_authorised_amount_is_refused()
    {
        var payment = TestData.PartiallyCaptured();   // EUR 40 of EUR 100 captured

        var result = payment.Capture(TestData.InEur(60.01m));

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrorCode.AmountExceedsAuthorised, result.Error.Code);
        Assert.Equal(TestData.InEur(40m), payment.CapturedTotal);
    }

    [Fact]
    public void Capturing_exactly_what_is_left_is_allowed()
    {
        var payment = TestData.PartiallyCaptured();   // EUR 40 of EUR 100 captured

        var result = payment.Capture(TestData.InEur(60m));

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
    }

    [Fact]
    public void Capturing_zero_is_refused()
    {
        var payment = TestData.Authorised();

        var result = payment.Capture(Money.Zero(TestData.Eur));

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrorCode.AmountNotPositive, result.Error.Code);
    }

    [Fact]
    public void Capturing_another_currency_is_refused()
    {
        var payment = TestData.Authorised();

        var result = payment.Capture(Money.Of(100m, TestData.Usd));

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrorCode.CurrencyMismatch, result.Error.Code);
        Assert.Equal(PaymentStatus.Authorised, payment.Status);
    }

    [Fact]
    public void Capture_records_one_operation_per_capture()
    {
        var payment = TestData.Authorised();

        payment.Capture(TestData.InEur(40m));
        payment.Capture(TestData.InEur(60m));

        var captures = payment.Operations.Where(o => o.Kind == PaymentAction.Capture).ToList();
        Assert.Equal(2, captures.Count);
        Assert.Equal(TestData.InEur(40m), captures[0].Amount);
        Assert.Equal(TestData.InEur(60m), captures[1].Amount);
    }

    [Fact]
    public void Capture_after_a_void_is_refused()
    {
        var payment = TestData.Voided();

        var result = payment.Capture(TestData.Eur100);

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrorCode.IllegalTransition, result.Error.Code);
    }
}
