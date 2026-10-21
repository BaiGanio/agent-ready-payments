using Payments.Domain;

namespace Payments.Api.Storage;

public interface IPaymentStore
{
    Task<Guid?> FindMerchantAsync(Guid merchantId, CancellationToken ct);

    Task<Loaded?> LoadAsync(Guid paymentId, CancellationToken ct);

    Task InsertAsync(Payment payment, Guid merchantId, Completion? completion, CancellationToken ct);

    Task UpdateAsync(Payment payment, int persistedOperationCount, Completion? completion, CancellationToken ct);
}

public sealed record Loaded(
    Payment Payment,
    Guid MerchantId,
    int OperationCount,
    DateTimeOffset UpdatedAt);