using Npgsql;
using Payments.Api.Storage;
using Payments.Domain;
using Xunit;

namespace Payments.Api.Tests;

/// <summary>
/// The key store's own contract: one row per claim, and what a second claim is told.
/// No HTTP here on purpose — these promises never show up in a response.
/// </summary>
public sealed class IdempotencyStoreTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    private const string StoredBody = """{ "id": 1, "status": "Created" }""";
    private static readonly Guid SecondMerchant = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly string Hash = new('a', 64);
    private static readonly string OtherHash = new('b', 64);

    [Fact]
    public async Task A_fresh_key_is_claimed()
    {
        var keys = IdempotencyStore();
        var ct = TestContext.Current.CancellationToken;

        var claim = await keys.ClaimAsync(SeededMerchant, "K", Hash, ct);

        Assert.IsType<Claim.Claimed>(claim);
        Assert.Equal(1, await CountRowsAsync("idempotency_key"));
        Assert.Equal("InProgress", await QuerySqlAsync<string>(
            "select status from idempotency_key where merchant_id = @Merchant and key = @Key",
            new { Merchant = SeededMerchant, Key = "K" }));
    }

    [Fact]
    public async Task A_second_claim_of_an_open_key_is_in_progress()
    {
        var keys = IdempotencyStore();
        var ct = TestContext.Current.CancellationToken;

        Assert.IsType<Claim.Claimed>(await keys.ClaimAsync(SeededMerchant, "K", Hash, ct));

        var second = await keys.ClaimAsync(SeededMerchant, "K", Hash, ct);

        Assert.IsType<Claim.InProgress>(second);
        Assert.Equal(1, await CountRowsAsync("idempotency_key"));
    }

    [Fact]
    public async Task A_claim_with_another_hash_is_a_mismatch()
    {
        var keys = IdempotencyStore();
        var ct = TestContext.Current.CancellationToken;

        Assert.IsType<Claim.Claimed>(await keys.ClaimAsync(SeededMerchant, "K", Hash, ct));

        var second = await keys.ClaimAsync(SeededMerchant, "K", OtherHash, ct);

        Assert.IsType<Claim.Mismatch>(second);
        Assert.Equal(Hash, await QuerySqlAsync<string>(
            "select request_hash from idempotency_key where merchant_id = @Merchant and key = @Key",
            new { Merchant = SeededMerchant, Key = "K" }));
    }

    [Fact]
    public async Task The_same_key_for_another_merchant_is_a_new_claim()
    {
        var keys = IdempotencyStore();
        var ct = TestContext.Current.CancellationToken;

        Assert.IsType<Claim.Claimed>(await keys.ClaimAsync(SeededMerchant, "K", Hash, ct));
        Assert.IsType<Claim.Claimed>(await keys.ClaimAsync(SecondMerchant, "K", Hash, ct));

        Assert.Equal(2, await CountRowsAsync("idempotency_key"));
    }

    [Fact]
    public async Task A_completed_key_replays_its_stored_answer()
    {
        var keys = IdempotencyStore();
        var ct = TestContext.Current.CancellationToken;

        Assert.IsType<Claim.Claimed>(await keys.ClaimAsync(SeededMerchant, "K", Hash, ct));

        await keys.CompleteAsync(new Completion(SeededMerchant, "K", 201, StoredBody, "/payments/1"), ct);

        var replay = Assert.IsType<Claim.Replay>(await keys.ClaimAsync(SeededMerchant, "K", Hash, ct));

        Assert.Equal(201, replay.StatusCode);
        Assert.Equal(StoredBody, replay.Body);
        Assert.Equal("/payments/1", replay.Location);
        Assert.Equal(1, await CountRowsAsync("idempotency_key"));
    }

    [Fact]
    public async Task A_released_key_can_be_claimed_again()
    {
        var keys = IdempotencyStore();
        var ct = TestContext.Current.CancellationToken;

        Assert.IsType<Claim.Claimed>(await keys.ClaimAsync(SeededMerchant, "K", Hash, ct));

        await keys.ReleaseAsync(SeededMerchant, "K", ct);

        Assert.IsType<Claim.Claimed>(await keys.ClaimAsync(SeededMerchant, "K", Hash, ct));
        Assert.Equal(1, await CountRowsAsync("idempotency_key"));
    }

    [Fact]
    public async Task The_completed_mark_rolls_back_with_the_payment_write()
    {
        var store = StoreFor("dapper");
        var keys = IdempotencyStore();
        var ct = TestContext.Current.CancellationToken;

        var payment = Payment.Create(Guid.CreateVersion7(), Money.Of(100m, "EUR"), new SteppingClock());

        await store.InsertAsync(payment, SeededMerchant, completion: null, ct);

        Assert.IsType<Claim.Claimed>(await keys.ClaimAsync(SeededMerchant, "K", Hash, ct));

        // The same payment id twice. The primary key on payment.id makes this insert fail,
        // and the Completed mark rides in the same transaction.
        await Assert.ThrowsAnyAsync<PostgresException>(() => store.InsertAsync(
            payment,
            SeededMerchant,
            new Completion(SeededMerchant, "K", 201, StoredBody, "/payments/1"),
            ct));

        Assert.Equal("InProgress", await QuerySqlAsync<string>(
            "select status from idempotency_key where merchant_id = @Merchant and key = @Key",
            new { Merchant = SeededMerchant, Key = "K" }));

        Assert.Null(await QuerySqlAsync<string?>(
            "select response_body from idempotency_key where merchant_id = @Merchant and key = @Key",
            new { Merchant = SeededMerchant, Key = "K" }));

        Assert.Equal(1, await CountRowsAsync("payment"));
    }
}