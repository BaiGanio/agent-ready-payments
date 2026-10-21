namespace Payments.Api.Storage;
public interface IIdempotencyStore
{
    Task<Claim> ClaimAsync(Guid merchantId, string key, string requestHash, CancellationToken ct);
    Task CompleteAsync(Completion completion, CancellationToken ct);     // an answer with no payment write: 409, 422
    Task ReleaseAsync(Guid merchantId, string key, CancellationToken ct);
}

public abstract record Claim
{
    public sealed record Claimed : Claim;
    public sealed record InProgress : Claim;
    public sealed record Mismatch : Claim;
    public sealed record Replay(int StatusCode, string Body, string? Location) : Claim;
}

public sealed record Completion(Guid MerchantId, string Key, int StatusCode, string Body, string? Location);