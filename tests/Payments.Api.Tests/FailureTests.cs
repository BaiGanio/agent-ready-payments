using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Payments.Api.Storage;
using Payments.Domain;
using Xunit;

namespace Payments.Api.Tests;

/// <summary>
/// A crash in the middle of the work. A 5xx is never stored: it is not an answer, it is a
/// missing answer. The key is released so the client's retry can really try again. Red until step 12.
/// </summary>
public sealed class FailureTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task A_crash_during_the_work_releases_the_key()
    {
        using var broken = BrokenFactory();
        var client = await ClientForAsync(broken);

        var response = await PostAsync("/payments", NewPayment(), NewKey(), client);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        // Nothing stored and nothing left open. A key stuck InProgress would block every retry.
        Assert.Equal(0, await CountRowsAsync("idempotency_key"));
        Assert.Equal(0, await CountRowsAsync("payment"));
    }

    [Fact]
    public async Task A_retry_after_a_crash_does_the_work()
    {
        string key = NewKey();
        var body = NewPayment();

        using var broken = BrokenFactory();
        var client = await ClientForAsync(broken);

        Assert.Equal(HttpStatusCode.InternalServerError,
            (await PostAsync("/payments", body, key, client)).StatusCode);

        // The same key, now through a healthy server. The release made the key claimable again.
        var retry = await PostAsync("/payments", body, key);

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(1, await CountRowsAsync("payment"));

        var dto = await retry.Content.ReadFromJsonAsync<PaymentDto>(TestContext.Current.CancellationToken);

        Assert.Equal("Created", dto!.Status);
    }

    // ----- helpers -----

    private static string NewKey() => Guid.NewGuid().ToString();

    private CreatePaymentRequest NewPayment(decimal amount = 100m) => new(SeededMerchant, amount, "EUR");

    /// <summary>
    /// A server whose payment store throws on InsertAsync. Everything else is real, including the
    /// key store, so the release in the handler's catch is the thing under test.
    /// </summary>
    private WebApplicationFactory<Program> BrokenFactory() =>
        Fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped<IPaymentStore>(_ => new ThrowingPaymentStore(
                    new DapperPaymentStore(Fixture.ConnectionString, TimeProvider.System)))));

    /// <summary>A client for that server, with the same API key and scopes as the normal one.</summary>
    private async Task<HttpClient> ClientForAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();

        client.DefaultRequestHeaders.Add("X-Api-Key",
            await InsertKeyAsync(["payments:read", "payments:write", "payments:refund"], "service", SeededMerchant));

        return client;
    }
}

/// <summary>
/// Every call goes to the real store except <see cref="InsertAsync"/>, which throws.
/// A test double, not a mock: it has no expectations, it just breaks in one place.
/// </summary>
internal sealed class ThrowingPaymentStore(IPaymentStore inner) : IPaymentStore
{
    public Task<Guid?> FindMerchantAsync(Guid merchantId, CancellationToken ct) =>
        inner.FindMerchantAsync(merchantId, ct);

    public Task<Loaded?> LoadAsync(Guid paymentId, CancellationToken ct) =>
        inner.LoadAsync(paymentId, ct);

    public Task InsertAsync(Payment payment, Guid merchantId, Completion? completion, CancellationToken ct) =>
        throw new InvalidOperationException("The database went away in the middle of the write.");

    public Task UpdateAsync(Payment payment, int persistedOperationCount, Completion? completion, CancellationToken ct) =>
        inner.UpdateAsync(payment, persistedOperationCount, completion, ct);
}
