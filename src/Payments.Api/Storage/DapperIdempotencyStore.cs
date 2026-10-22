using System.Data.Common;
using Dapper;
using Npgsql;

namespace Payments.Api.Storage;

public sealed class DapperIdempotencyStore(string connectionString) : IIdempotencyStore
{
    private const string AnswerColumns = """
    request_hash      as "RequestHash",
    status            as "Status",
    response_code     as "ResponseCode",
    response_body     as "ResponseBody",
    response_location as "ResponseLocation"
    """;

    /// <summary>
    /// Insert first, ask later. There is no select before the insert, because a select that says
    /// "not there" is already out of date on the next line. The primary key decides the winner.
    /// </summary>
    public async Task<Claim> ClaimAsync(Guid merchantId, string key, string requestHash, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);

        // A row back means this request owns the key. No row means someone else does.
        var claimed = await conn.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            """
            insert into idempotency_key (merchant_id, key, request_hash, status)
            values (@MerchantId, @Key, @RequestHash, 'InProgress')
            on conflict (merchant_id, key) do nothing
            returning key
            """,
            new { MerchantId = merchantId, Key = key, RequestHash = requestHash },
            cancellationToken: ct));

        if (claimed is not null)
            return new Claim.Claimed();

        var row = await conn.QuerySingleOrDefaultAsync<AnswerRow>(new CommandDefinition(
            $"""
            select {AnswerColumns}
            from idempotency_key
            where merchant_id = @MerchantId and key = @Key
            """,
            new { MerchantId = merchantId, Key = key },
            cancellationToken: ct));

        // The owner released the key between the two statements. Say "in progress":
        // the client retries, and the retry claims it.
        if (row is null)
            return new Claim.InProgress();

        // The hash comes before the status. A key sent with another body is a client bug
        // whatever the first request is doing, and it must be loud either way.
        if (!string.Equals(row.RequestHash, requestHash, StringComparison.Ordinal))
            return new Claim.Mismatch();

        if (row.Status == InProgress)
            return new Claim.InProgress();

        // The table's check constraint promises a Completed row has a code and a body.
        return new Claim.Replay(row.ResponseCode!.Value, row.ResponseBody!, row.ResponseLocation);
    }

    /// <summary>For an answer with no payment write — a 409 or a 422. Its own connection, one statement.</summary>
    public async Task CompleteAsync(Completion completion, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);

        await MarkCompletedAsync(conn, tx: null, completion, ct);
    }

    /// <summary>
    /// Only an open key goes away. A Completed key keeps its stored answer for ever:
    /// deleting it would turn a replay into a second charge.
    /// </summary>
    public async Task ReleaseAsync(Guid merchantId, string key, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);

        await conn.ExecuteAsync(new CommandDefinition(
            """
            delete from idempotency_key
            where merchant_id = @MerchantId and key = @Key and status = 'InProgress'
            """,
            new { MerchantId = merchantId, Key = key },
            cancellationToken: ct));
    }

    /// <summary>
    /// The Completed mark. One conditional update that must change exactly one row.
    /// Zero rows means someone released or completed the key underneath us, so the caller's
    /// transaction must not commit. <paramref name="tx"/> is how the payment write rides along:
    /// pass its transaction and the mark lives and dies with the money.
    /// </summary>
    internal static async Task MarkCompletedAsync(
        NpgsqlConnection conn,
        DbTransaction? tx,
        Completion completion,
        CancellationToken ct)
    {
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            """
            update idempotency_key
               set status            = 'Completed',
                   response_code     = @StatusCode,
                   response_body     = @Body,
                   response_location = @Location,
                   completed_at      = now()
             where merchant_id = @MerchantId and key = @Key and status = 'InProgress'
            """,
            new
            {
                completion.MerchantId,
                completion.Key,
                completion.StatusCode,
                completion.Body,
                completion.Location
            },
            tx,
            cancellationToken: ct));

        if (affected != 1)
            throw new InvalidOperationException(
                $"Idempotency key '{completion.Key}' was not InProgress when completing it.");
    }

    private const string InProgress = "InProgress";

    private sealed record AnswerRow(
        string RequestHash,
        string Status,
        int? ResponseCode,
        string? ResponseBody,
        string? ResponseLocation);
}
