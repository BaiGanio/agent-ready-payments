namespace Payments.Api.Storage;

public sealed class DapperIdempotencyStore(string connectionString) : IIdempotencyStore
{
    public Task<Claim> ClaimAsync(Guid merchantId, string key, string requestHash, CancellationToken ct)
        => throw new NotImplementedException();

    public Task CompleteAsync(Completion completion, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    public Task ReleaseAsync(Guid merchantId, string key, CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}