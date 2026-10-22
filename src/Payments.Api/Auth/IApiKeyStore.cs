namespace Payments.Api.Auth
{
    public interface IApiKeyStore
    {
        /// <summary>The caller for a live key. Null when the key is unknown or revoked.</summary>
        Task<ApiCaller?> FindAsync(string keyHash, CancellationToken ct);
    }
}