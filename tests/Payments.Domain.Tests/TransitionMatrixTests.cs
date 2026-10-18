namespace Payments.Domain.Tests;

/// <summary>
/// The whole transition table in one place: 8 statuses x 5 actions = 40 cases.
/// One row of the table is one test case, so a wrong row names itself in the output.
/// </summary>
public class TransitionMatrixTests
{
    /// <summary>Every pair that is allowed. Everything not in this set must be refused.</summary>
    private static readonly HashSet<(PaymentStatus, PaymentAction)> Allowed =
    [
        (PaymentStatus.Created, PaymentAction.Authorise),
        (PaymentStatus.Created, PaymentAction.Decline),
        (PaymentStatus.Authorised, PaymentAction.Capture),
        (PaymentStatus.Authorised, PaymentAction.Void),
        (PaymentStatus.PartiallyCaptured, PaymentAction.Capture),
        (PaymentStatus.PartiallyCaptured, PaymentAction.Refund),
        (PaymentStatus.Captured, PaymentAction.Refund),
        (PaymentStatus.PartiallyRefunded, PaymentAction.Refund),
    ];

    public static TheoryData<PaymentStatus, PaymentAction, bool> Matrix()
    {
        var data = new TheoryData<PaymentStatus, PaymentAction, bool>();

        foreach (var status in Enum.GetValues<PaymentStatus>())
        {
            foreach (var action in Enum.GetValues<PaymentAction>())
            {
                data.Add(status, action, Allowed.Contains((status, action)));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Only_the_table_moves_are_possible(PaymentStatus status, PaymentAction action, bool allowed)
    {
        var payment = TestData.InStatus(status);

        var result = Invoke(payment, action);

        Assert.Equal(allowed, result.IsSuccess);

        if (!allowed)
        {
            Assert.Equal(PaymentErrorCode.IllegalTransition, result.Error.Code);
        }
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void A_refused_move_changes_nothing(PaymentStatus status, PaymentAction action, bool allowed)
    {
        if (allowed)
        {
            return;
        }

        var payment = TestData.InStatus(status);
        var operationsBefore = payment.Operations.Count;
        var capturedBefore = payment.CapturedTotal;
        var refundedBefore = payment.RefundedTotal;

        Invoke(payment, action);

        Assert.Equal(status, payment.Status);
        Assert.Equal(operationsBefore, payment.Operations.Count);
        Assert.Equal(capturedBefore, payment.CapturedTotal);
        Assert.Equal(refundedBefore, payment.RefundedTotal);
    }

    [Theory]
    [InlineData(PaymentStatus.Declined)]
    [InlineData(PaymentStatus.Voided)]
    [InlineData(PaymentStatus.Refunded)]
    public void A_terminal_status_allows_nothing(PaymentStatus status)
    {
        var payment = TestData.InStatus(status);

        Assert.Empty(payment.AllowedActions);
    }

    /// <summary>EUR 1 is inside the limit for every legal capture or refund in the table.</summary>
    private static Result<Payment, PaymentError> Invoke(Payment payment, PaymentAction action) => action switch
    {
        PaymentAction.Authorise => payment.Authorise(),
        PaymentAction.Decline => payment.Decline("issuer said no"),
        PaymentAction.Capture => payment.Capture(TestData.InEur(1m)),
        PaymentAction.Void => payment.Void(),
        PaymentAction.Refund => payment.Refund(TestData.InEur(1m)),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action in the test matrix."),
    };
}
