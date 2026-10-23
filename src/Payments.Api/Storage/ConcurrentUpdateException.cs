namespace Payments.Api.Storage;

/// <summary>
/// The payment moved between the load and the write, so the write was refused and rolled back.
/// It is not an error the caller can be told about: the decision was made on data that is no
/// longer true. CommandAsync catches it once, loads the payment again and re-runs the same
/// command, so the answer comes from the domain and not from the plumbing.
/// </summary>
public sealed class ConcurrentUpdateException(Guid paymentId)
    : Exception($"Payment {paymentId} changed while this request was deciding what to do.")
{
    public Guid PaymentId { get; } = paymentId;
}