namespace Payments.Api.Storage
{
    /// <summary>
    /// Reads for the list screen. Separate from <see cref="IPaymentStore"/> on purpose:
    /// that one loads a payment and replays it through the state machine, which a list does not need.
    /// </summary>
    public interface IPaymentQueries
    {
        Task<PaymentList> ListAsync(Guid merchantId, string? status, int limit, CancellationToken ct);
    }
}
