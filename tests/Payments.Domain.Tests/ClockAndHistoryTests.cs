namespace Payments.Domain.Tests;

/// <summary>
/// The domain must never read the wall clock itself. Every timestamp comes from the
/// injected TimeProvider, so a test can say exactly what "now" is.
/// </summary>
public class ClockAndHistoryTests
{
    [Fact]
    public void Created_at_comes_from_the_injected_clock()
    {
        var clock = new FixedClock();

        var payment = TestData.NewPayment(clock);

        Assert.Equal(FixedClock.Default, payment.CreatedAt);
    }

    [Fact]
    public void Each_operation_is_stamped_with_the_time_of_the_clock()
    {
        var clock = new FixedClock();
        var payment = TestData.NewPayment(clock);

        payment.Authorise();
        clock.Advance(TimeSpan.FromHours(2));
        payment.Capture(TestData.Eur100);

        Assert.Equal(FixedClock.Default, payment.Operations[0].At);
        Assert.Equal(FixedClock.Default.AddHours(2), payment.Operations[1].At);
    }

    [Fact]
    public void The_history_is_in_the_order_things_happened()
    {
        var payment = TestData.NewPayment();

        payment.Authorise();
        payment.Capture(TestData.InEur(40m));
        payment.Capture(TestData.InEur(60m));
        payment.Refund(TestData.InEur(100m));

        Assert.Equal(
            [PaymentAction.Authorise, PaymentAction.Capture, PaymentAction.Capture, PaymentAction.Refund],
            payment.Operations.Select(o => o.Kind));
    }

    [Fact]
    public void The_history_cannot_be_changed_from_outside()
    {
        var payment = TestData.Captured();

        var asCollection = Assert.IsAssignableFrom<ICollection<PaymentOperation>>(payment.Operations);

        Assert.True(asCollection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => asCollection.Add(payment.Operations[0]));
    }

    [Fact]
    public void Create_refuses_a_missing_clock()
    {
        Assert.Throws<ArgumentNullException>(() => Payment.Create(Guid.NewGuid(), TestData.Eur100, null!));
    }
}
