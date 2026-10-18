namespace Payments.Domain.Tests;

/// <summary>
/// An agent asks "what can I do next?" and must get a deterministic answer.
/// AllowedActions is that answer, and it must agree with what the methods really do.
/// </summary>
public class AllowedActionsTests
{
    [Fact]
    public void A_created_payment_can_be_authorised_or_declined()
    {
        var payment = TestData.NewPayment();

        Assert.Equal(
            [PaymentAction.Authorise, PaymentAction.Decline],
            payment.AllowedActions.Order());
    }

    [Fact]
    public void An_authorised_payment_can_be_captured_or_voided()
    {
        var payment = TestData.Authorised();

        Assert.Equal(
            [PaymentAction.Capture, PaymentAction.Void],
            payment.AllowedActions.Order());
    }

    [Fact]
    public void A_partially_captured_payment_can_be_captured_or_refunded()
    {
        var payment = TestData.PartiallyCaptured();

        Assert.Equal(
            [PaymentAction.Capture, PaymentAction.Refund],
            payment.AllowedActions.Order());
    }

    [Fact]
    public void A_captured_payment_can_only_be_refunded()
    {
        var payment = TestData.Captured();

        Assert.Equal([PaymentAction.Refund], payment.AllowedActions.Order());
    }

    [Fact]
    public void A_partially_refunded_payment_can_only_be_refunded()
    {
        var payment = TestData.PartiallyRefunded();

        Assert.Equal([PaymentAction.Refund], payment.AllowedActions.Order());
    }

    [Theory]
    [InlineData(PaymentStatus.Declined)]
    [InlineData(PaymentStatus.Voided)]
    [InlineData(PaymentStatus.Refunded)]
    public void A_terminal_payment_allows_nothing(PaymentStatus status)
    {
        var payment = TestData.InStatus(status);

        Assert.Empty(payment.AllowedActions);
    }

    [Fact]
    public void Remaining_amounts_tell_an_agent_how_much_it_may_ask_for()
    {
        var payment = TestData.PartiallyCaptured();   // EUR 100 authorised, EUR 40 captured

        Assert.Equal(TestData.InEur(60m), payment.CapturableRemaining);
        Assert.Equal(TestData.InEur(40m), payment.RefundableRemaining);
    }

    [Fact]
    public void Nothing_is_capturable_or_refundable_on_a_voided_payment()
    {
        var payment = TestData.Voided();

        Assert.True(payment.CapturableRemaining.IsZero);
        Assert.True(payment.RefundableRemaining.IsZero);
    }
}
