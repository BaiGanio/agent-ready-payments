using Npgsql;
using Payments.Api.Storage;
using Payments.Domain;
using Xunit;

namespace Payments.Api.Tests;

/// <summary>
/// The only tests allowed to call a store directly, because the store's own
/// contract is what they check. Every test runs once per engine.
/// </summary>
public sealed class StoreTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Theory]
    [InlineData("dapper")]
    public async Task A_round_trip_keeps_every_amount_and_timestamp(string engine)
    {
        var store = StoreFor(engine);
        var clock = new SteppingClock();
        var ct = TestContext.Current.CancellationToken;

        var payment = Payment.Create(Guid.CreateVersion7(), Money.Of(100m, "EUR"), clock);
        Assert.True(payment.Authorise().IsSuccess);

        await store.InsertAsync(payment, SeededMerchant, completion: null, ct);

        Assert.True(payment.Capture(Money.Of(40m, "EUR")).IsSuccess);

        await store.UpdateAsync(payment, persistedOperationCount: 1, completion: null, ct);

        var loaded = await store.LoadAsync(payment.Id, ct);

        Assert.NotNull(loaded);
        Assert.Equal(SeededMerchant, loaded.MerchantId);
        Assert.Equal(2, loaded.OperationCount);

        var reloaded = loaded.Payment;

        Assert.Equal(payment.Id, reloaded.Id);
        Assert.Equal(payment.Status, reloaded.Status);
        Assert.Equal(payment.Amount, reloaded.Amount);
        Assert.Equal(payment.CapturedTotal, reloaded.CapturedTotal);
        Assert.Equal(payment.RefundedTotal, reloaded.RefundedTotal);
        Assert.Equal(payment.CreatedAt, reloaded.CreatedAt);
        Assert.Equal(payment.Operations, reloaded.Operations);
    }

    [Theory]
    [InlineData("dapper")]
    public async Task A_replayed_payment_matches_the_stored_status(string engine)
    {
        var store = StoreFor(engine);
        var ct = TestContext.Current.CancellationToken;

        var id = Guid.CreateVersion7();
        var at = SteppingClock.Default;

        await ExecuteSqlAsync(
            """
            insert into payment (id, merchant_id, status, currency, authorised_amount,
                                 captured_amount, refunded_amount, created_at, updated_at)
            values (@Id, @MerchantId, 'Captured', 'EUR', 100, 0, 0, @At, @At);

            insert into payment_operation (id, payment_id, kind, amount, at)
            values (@OperationId, @Id, 'Authorise', 100, @At);
            """,
            new { Id = id, MerchantId = SeededMerchant, OperationId = Guid.CreateVersion7(), At = at });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.LoadAsync(id, ct));

        Assert.Contains("replayed to", error.Message);
    }

    [Theory]
    [InlineData("dapper")]
    public async Task Update_inserts_only_the_new_operations(string engine)
    {
        var store = StoreFor(engine);
        var clock = new SteppingClock();
        var ct = TestContext.Current.CancellationToken;

        var payment = Payment.Create(Guid.CreateVersion7(), Money.Of(100m, "EUR"), clock);
        Assert.True(payment.Authorise().IsSuccess);
        Assert.True(payment.Capture(Money.Of(30m, "EUR")).IsSuccess);

        await store.InsertAsync(payment, SeededMerchant, completion: null, ct);

        Assert.Equal(2, await CountRowsAsync("payment_operation"));

        var loaded = await store.LoadAsync(payment.Id, ct);

        Assert.NotNull(loaded);
        Assert.True(loaded.Payment.Capture(Money.Of(20m, "EUR")).IsSuccess);

        await store.UpdateAsync(loaded.Payment, loaded.OperationCount, completion: null, ct);

        Assert.Equal(3, await CountRowsAsync("payment_operation"));
    }

    [Theory]
    [InlineData("dapper")]
    public async Task A_failed_update_leaves_nothing_behind(string engine)
    {
        var store = StoreFor(engine);
        var clock = new SteppingClock();
        var ct = TestContext.Current.CancellationToken;

        var payment = Payment.Create(Guid.CreateVersion7(), Money.Of(100m, "EUR"), clock);
        Assert.True(payment.Authorise().IsSuccess);

        await store.InsertAsync(payment, SeededMerchant, completion: null, ct);

        // The only check on payment_operation is the kind list, and the domain never
        // breaks it. So refuse the insert at the database instead.
        await ExecuteSqlAsync(
            """
            create function test_block_operation_insert() returns trigger
            language plpgsql as $$ begin raise exception 'blocked by the test'; end $$;

            create trigger test_block_operation_insert
            before insert on payment_operation
            for each row execute function test_block_operation_insert();
            """);

        try
        {
            Assert.True(payment.Capture(Money.Of(40m, "EUR")).IsSuccess);

            await Assert.ThrowsAsync<PostgresException>(
                () => store.UpdateAsync(payment, persistedOperationCount: 1, completion: null, ct));

            Assert.Equal(1, await CountRowsAsync("payment_operation"));
            Assert.Equal("Authorised", await QuerySqlAsync<string>(
                "select status from payment where id = @Id", new { payment.Id }));
            Assert.Equal(0m, await QuerySqlAsync<decimal>(
                "select captured_amount from payment where id = @Id", new { payment.Id }));
        }
        finally
        {
            await ExecuteSqlAsync(
                """
                drop trigger if exists test_block_operation_insert on payment_operation;
                drop function if exists test_block_operation_insert();
                """);
        }
    }
}