using Dapper;
using Npgsql;
using Payments.Domain;

namespace Payments.Api.Storage;
public sealed class DapperPaymentStore(string connectionString) : IPaymentStore
{
    private const string PaymentColumns = """
    id                as "Id",
    merchant_id       as "MerchantId",
    status            as "Status",
    currency          as "Currency",
    authorised_amount as "AuthorisedAmount",
    captured_amount   as "CapturedAmount",
    refunded_amount   as "RefundedAmount",
    decline_reason    as "DeclineReason",
    created_at        as "CreatedAt",
    updated_at        as "UpdatedAt"
    """;
    public async Task<Guid?> FindMerchantAsync(Guid merchantId, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);

        return await conn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "select id from merchant where id = @Id", new { Id = merchantId }, cancellationToken: ct));
    }

    public async Task InsertAsync(Payment payment, Guid merchantId, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(
            """
            insert into payment (id, merchant_id, status, currency, authorised_amount,
                                 captured_amount, refunded_amount, decline_reason,
                                 created_at, updated_at)
            values (@Id, @MerchantId, @Status, @Currency, @AuthorisedAmount,
                    @CapturedAmount, @RefundedAmount, @DeclineReason,
                    @CreatedAt, @UpdatedAt)
            """,
            new
            {
                payment.Id,
                MerchantId = merchantId,
                Status = payment.Status.ToString(),
                Currency = payment.Amount.Currency,
                AuthorisedAmount = payment.Amount.Amount,
                CapturedAmount = payment.CapturedTotal.Amount,
                RefundedAmount = payment.RefundedTotal.Amount,
                payment.DeclineReason,
                payment.CreatedAt,
                UpdatedAt = payment.CreatedAt
            },
            tx,
            cancellationToken: ct));

        await InsertOperationsAsync(conn, tx, payment, 0, ct);

        await tx.CommitAsync(ct);
    }

    public async Task<Loaded?> LoadAsync(Guid paymentId, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);

        var row = await conn.QuerySingleOrDefaultAsync<PaymentRow>(new CommandDefinition(
            $"select {PaymentColumns} from payment where id = @Id", new { Id = paymentId }, cancellationToken: ct));

        if (row is null) return null;

         var operations = (await conn.QueryAsync<OperationRow>(new CommandDefinition(
            """
            select kind as "Kind", amount as "Amount", at as "At"
            from payment_operation
            where payment_id = @Id
            order by at, id
            """,
            new { Id = paymentId },
            cancellationToken: ct))).AsList();

        var clock = new ReplayClock([new DateTimeOffset(row.CreatedAt), .. operations.Select(o =>  new DateTimeOffset(o.At))]);
        var payment = Payment.Create(
            row.Id,
            Money.Of(row.AuthorisedAmount, row.Currency),
            clock);

        foreach (var operation in operations)
        {
            var money = Money.Of(operation.Amount, row.Currency);

            var result = operation.Kind switch
            {
                nameof(PaymentAction.Authorise) => payment.Authorise(),
                nameof(PaymentAction.Decline) => payment.Decline(row.DeclineReason ?? "unknown"),
                nameof(PaymentAction.Capture) => payment.Capture(money),
                nameof(PaymentAction.Void) => payment.Void(),
                nameof(PaymentAction.Refund) => payment.Refund(money),
                _ => throw new InvalidOperationException(
                    $"Payment {row.Id} has an unknown operation kind '{operation.Kind}'")
            };

            if (result.IsFailure)
                throw new InvalidOperationException(
                    $"Payment {row.Id} cannot be replayed: {operation.Kind} was refused " +
                    $"with {result.Error.Code} ({result.Error.Message})");
        }

        if (payment.Status.ToString() != row.Status)
            throw new InvalidOperationException(
                $"Payment {row.Id} replayed to {payment.Status} but the row says {row.Status}");

        return new Loaded(payment, row.MerchantId, operations.Count, new DateTimeOffset(row.UpdatedAt));
    }

    public async Task UpdateAsync(Payment payment, int persistedOperationCount, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var updatedAt = payment.Operations.Count > 0
            ? payment.Operations[^1].At
            : payment.CreatedAt;

        var affected = await conn.ExecuteAsync(new CommandDefinition(
            """
            update payment
               set status          = @Status,
                   captured_amount = @CapturedAmount,
                   refunded_amount = @RefundedAmount,
                   decline_reason  = @DeclineReason,
                   updated_at      = @UpdatedAt
             where id = @Id
            """,
            new
            {
                payment.Id,
                Status = payment.Status.ToString(),
                CapturedAmount = payment.CapturedTotal.Amount,
                RefundedAmount = payment.RefundedTotal.Amount,
                payment.DeclineReason,
                UpdatedAt = updatedAt
            },
            tx,
            cancellationToken: ct));

        if (affected != 1)
            throw new InvalidOperationException(
                $"Payment {payment.Id} was not found when updating");

        await InsertOperationsAsync(conn, tx, payment, persistedOperationCount, ct);

        await tx.CommitAsync(ct);
    }

    private static async Task InsertOperationsAsync(
        NpgsqlConnection conn,
        System.Data.Common.DbTransaction tx,
        Payment payment,
        int skip,
        CancellationToken ct)
    {
        var rows = payment.Operations
            .Skip(skip)
            .Select(o => new
            {
                Id = Guid.CreateVersion7(),
                PaymentId = payment.Id,
                Kind = o.Kind.ToString(),
                Amount = o.Amount.Amount,
                o.At
            })
            .ToArray();

        if (rows.Length == 0)
            return;

        await conn.ExecuteAsync(new CommandDefinition(
            """
            insert into payment_operation (id, payment_id, kind, amount, at)
            values (@Id, @PaymentId, @Kind, @Amount, @At)
            """,
            rows,
            tx,
            cancellationToken: ct));
    }

    private sealed record PaymentRow(
        Guid Id,
        Guid MerchantId,
        string Status,
        string Currency,
        decimal AuthorisedAmount,
        decimal CapturedAmount,
        decimal RefundedAmount,
        string? DeclineReason,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    private sealed record OperationRow(string Kind, decimal Amount, DateTime At);
}
