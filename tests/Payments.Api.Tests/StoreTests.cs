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

        await store.InsertAsync(payment, SeededMerchant, ct);

        Assert.True(payment.Capture(Money.Of(40m, "EUR")).IsSuccess);

        await store.UpdateAsync(payment, persistedOperationCount: 1, ct);

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
}