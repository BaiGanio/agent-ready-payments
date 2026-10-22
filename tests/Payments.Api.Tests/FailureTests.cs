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
/// missing answer. The key is released so the client's retry can really try again.
///
/// Both write paths are covered, because they are different code. A create crashes inside
/// <c>InsertAsync</c> and the release is in the create handler's own catch. A command
/// crashes inside <c>UpdateAsync</c> and the release is in the catch in <c>CommandAsync</c>,
/// which had no test at all until <c>CF-036</c> was closed. The refund pair is the one that
/// matters most: on that path a key left <c>InProgress</c> blocks the retry, and a key
/// wrongly marked <c>Completed</c> would replay a refund that never happened.
/// </summary>
public sealed class FailureTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task A_crash_during_the_work_releases_the_key()
    {
        using var broken = BrokenFactory(ThrowOn.Insert);
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

        using var broken = BrokenFactory(ThrowOn.Insert);
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

    /// <summary>
    /// CF-036. The same promise as the create crash, on the update path. The payment already
    /// exists here, so a stuck key is worse: the refund is still owed, and every retry of it
    /// would answer 409 for ever with nobody able to clear the row.
    /// </summary>
    [Fact]
    public async Task A_crash_during_a_refund_releases_the_key()
    {
        // Arranged through the healthy client: create, authorise and capture must really work.
        var payment = await CapturedPaymentAsync(amount: 100m);

        long keysBefore = await CountRowsAsync("idempotency_key");

        using var broken = BrokenFactory(ThrowOn.Update);
        var client = await ClientForAsync(broken);

        string key = NewKey();

        var response = await PostAsync($"/payments/{payment.Id}/refund", new AmountRequest(40m, "EUR"), key, client);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        // The refund's own key is gone. Count by key, not by table: the three keys from
        // create, authorise and capture are legitimately still there.
        Assert.Equal(0, await QuerySqlAsync<long>(
            "select count(*) from idempotency_key where key = @key", new { key }));

        Assert.Equal(keysBefore, await CountRowsAsync("idempotency_key"));

        // And no money moved.
        Assert.Equal(0, await CountOperationsAsync(payment.Id, "Refund"));
        Assert.Equal(0m, await QuerySqlAsync<decimal>(
            "select refunded_amount from payment where id = @Id", new { payment.Id }));
    }

    /// <summary>
    /// The one that costs money. After a crashed refund the client retries with the same
    /// Idempotency-Key. Exactly one refund must land: not none, because the money is owed,
    /// and not two, because the first attempt never happened.
    /// </summary>
    [Fact]
    public async Task A_retry_after_a_crashed_refund_refunds_once()
    {
        var payment = await CapturedPaymentAsync(amount: 100m);

        string key = NewKey();
        var body = new AmountRequest(40m, "EUR");
        string path = $"/payments/{payment.Id}/refund";

        using var broken = BrokenFactory(ThrowOn.Update);
        var client = await ClientForAsync(broken);

        Assert.Equal(HttpStatusCode.InternalServerError,
            (await PostAsync(path, body, key, client)).StatusCode);

        // The same key, now through the healthy server.
        var retry = await PostAsync(path, body, key);

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);

        // Not a replay. A 5xx is never stored, so there was no stored answer to replay.
        Assert.False(retry.Headers.Contains("Idempotent-Replayed"));

        var dto = await retry.Content.ReadFromJsonAsync<PaymentDto>(TestContext.Current.CancellationToken);

        Assert.Equal(40m, dto!.RefundedAmount);
        Assert.Equal(60m, dto.RefundableRemaining);
        Assert.Equal("PartiallyRefunded", dto.Status);

        // Once. Not twice.
        Assert.Equal(1, await CountOperationsAsync(payment.Id, "Refund"));
        Assert.Equal(40m, await QuerySqlAsync<decimal>(
            "select refunded_amount from payment where id = @Id", new { payment.Id }));
    }

    // ----- helpers -----

    private static string NewKey() => Guid.NewGuid().ToString();

    private CreatePaymentRequest NewPayment(decimal amount = 100m) => new(SeededMerchant, amount, "EUR");

    /// <summary>
    /// A server whose payment store throws on one write. Everything else is real, including the
    /// key store, so the release in the handler's catch is the thing under test.
    /// </summary>
    private WebApplicationFactory<Program> BrokenFactory(ThrowOn where) =>
        Fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped<IPaymentStore>(_ => new ThrowingPaymentStore(
                    new DapperPaymentStore(Fixture.ConnectionString, TimeProvider.System), where))));

    /// <summary>A client for that server, with the same API key and scopes as the normal one.</summary>
    private async Task<HttpClient> ClientForAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();

        client.DefaultRequestHeaders.Add("X-Api-Key",
            await InsertKeyAsync(["payments:read", "payments:write", "payments:refund"], "service", SeededMerchant));

        return client;
    }
}

/// <summary>Which write breaks. Create goes through Insert; authorise, capture and refund through Update.</summary>
internal enum ThrowOn { Insert, Update }

/// <summary>
/// Every call goes to the real store except the one named by <see cref="ThrowOn"/>, which throws.
/// A test double, not a mock: it has no expectations, it just breaks in one place.
/// Breaking only one at a time matters -- the rest of the flow has to be real, or the
/// release under test would never be reached.
/// </summary>
internal sealed class ThrowingPaymentStore(IPaymentStore inner, ThrowOn where) : IPaymentStore
{
    private static Task Broken() =>
        throw new InvalidOperationException("The database went away in the middle of the write.");

    public Task<Guid?> FindMerchantAsync(Guid merchantId, CancellationToken ct) =>
        inner.FindMerchantAsync(merchantId, ct);

    public Task<Loaded?> LoadAsync(Guid paymentId, CancellationToken ct) =>
        inner.LoadAsync(paymentId, ct);

    public Task InsertAsync(Payment payment, Guid merchantId, Completion? completion, CancellationToken ct) =>
        where == ThrowOn.Insert
            ? Broken()
            : inner.InsertAsync(payment, merchantId, completion, ct);

    public Task UpdateAsync(Payment payment, int persistedOperationCount, Completion? completion, CancellationToken ct) =>
        where == ThrowOn.Update
            ? Broken()
            : inner.UpdateAsync(payment, persistedOperationCount, completion, ct);
}
