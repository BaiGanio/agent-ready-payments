using System.Net;
using Xunit;

namespace Payments.Api.Tests;

/// <summary>One merchant per key. Another merchant's payment does not exist for you. Red until step 4.</summary>
public sealed class MerchantBindingTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Another_merchants_payment_is_404()
    {
        var payment = await CreatePaymentAsync();
        using var other = await ClientWith(["payments:read"], merchant: OtherMerchant);

        var response = await other.GetAsync($"/payments/{payment.Id}", TestContext.Current.CancellationToken);

        // The same answer as an id that does not exist. A 403 would say "it exists, it is not yours".
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("payment_not_found", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Another_merchants_payment_cannot_be_refunded()
    {
        var captured = await CapturedPaymentAsync(100m);
        using var other = await ClientWith(["payments:read", "payments:refund"], merchant: OtherMerchant);

        var response = await PostAsync($"/payments/{captured.Id}/refund", new AmountRequest(50m, "EUR"), client: other);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await QuerySqlAsync<long>(
            "select count(*) from payment_operation where payment_id = @Id and kind = 'Refund'", new { captured.Id }));
    }

    [Fact]
    public async Task Create_for_another_merchant_is_403_merchant_mismatch()
    {
        // The default Client holds an M1 service key with payments:write.
        var response = await PostAsync("/payments", new CreatePaymentRequest(OtherMerchant, 100m, "EUR"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("merchant_mismatch", await ErrorCodeAsync(response));
        Assert.Equal(0, await CountRowsAsync("payment"));
    }
}
