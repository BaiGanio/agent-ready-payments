using Dapper;
using Npgsql;

namespace Payments.Api.Auth;

public sealed class DapperApiKeyStore(string connectionString) : IApiKeyStore
{
    public async Task<ApiCaller?> FindAsync(string keyHash, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);

        var row = await conn.QuerySingleOrDefaultAsync<KeyRow>(new CommandDefinition(
            """
            select name        as "Name",
                   merchant_id as "MerchantId",
                   kind        as "Kind",
                   scopes      as "Scopes"
            from api_key
            where key_hash = @KeyHash and revoked_at is null
            """,
            new { KeyHash = keyHash }, cancellationToken: ct));

        return row is null ? null : new ApiCaller(row.Name, row.MerchantId, row.Kind, row.Scopes.ToHashSet());
    }

    // Dapper reads text[] as string[]. It cannot fill an IReadOnlySet, so it lands here first.
    private sealed class KeyRow
    {
        public string Name { get; init; } = "";
        public Guid MerchantId { get; init; }
        public string Kind { get; init; } = "";
        public string[] Scopes { get; init; } = [];
    }
}