using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Payments.Api.Tests;

/// <summary>
/// One key, two different requests. That is a client bug, and it must be loud.
/// Handing back the first payment for a request that asked for something else would hide
/// the bug until it costs money. Red until step 10.
/// </summary>
public sealed class KeyReuseTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task The_same_key_with_another_amount_is_422_idempotency_key_reused()
    {
        string key = NewKey();

        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(key, 100m)).StatusCode);

        var second = await CreateAsync(key, 200m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        Assert.Equal("idempotency_key_reused", await ErrorCodeAsync(second));
    }

    [Fact]
    public async Task The_same_key_on_another_endpoint_is_422()
    {
        // The path is part of the hash, so capture and refund with one key is a mismatch.
        string key = NewKey();

        var authorised = await AuthorisedPaymentAsync(100m);

        var capture = await PostAsync($"/payments/{authorised.Id}/capture", new AmountRequest(100m, "EUR"), key);

        Assert.Equal(HttpStatusCode.OK, capture.StatusCode);

        var refund = await PostAsync($"/payments/{authorised.Id}/refund", new AmountRequest(100m, "EUR"), key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refund.StatusCode);
        Assert.Equal("idempotency_key_reused", await ErrorCodeAsync(refund));
    }

    [Fact]
    public async Task The_same_key_on_another_payment_is_422()
    {
        // Same merchant, same endpoint, same amount — but another id, so another hash.
        string key = NewKey();

        var a = await CapturedPaymentAsync(100m);
        var b = await CapturedPaymentAsync(100m);

        var first = await PostAsync($"/payments/{a.Id}/refund", new AmountRequest(40m, "EUR"), key);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await PostAsync($"/payments/{b.Id}/refund", new AmountRequest(40m, "EUR"), key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        Assert.Equal("idempotency_key_reused", await ErrorCodeAsync(second));
    }

    [Fact]
    public async Task A_reused_key_changes_nothing()
    {
        // The 422 is a refusal, not a rewrite. The stored answer still belongs to the first body.
        string key = NewKey();

        var first = await CreateAsync(key, 100m);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        long payments = await CountRowsAsync("payment");
        long keys = await CountRowsAsync("idempotency_key");

        var reused = await CreateAsync(key, 200m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reused.StatusCode);
        Assert.Equal(payments, await CountRowsAsync("payment"));
        Assert.Equal(keys, await CountRowsAsync("idempotency_key"));

        // The first body again: still a replay of the first answer, word for word.
        var replay = await CreateAsync(key, 100m);

        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(await TextAsync(first), await TextAsync(replay));
    }

    // ----- helpers -----

    private static string NewKey() => Guid.NewGuid().ToString();

    private Task<HttpResponseMessage> CreateAsync(string key, decimal amount) =>
        PostAsync("/payments", new CreatePaymentRequest(SeededMerchant, amount, "EUR"), key);

    private static Task<string> TextAsync(HttpResponseMessage response) =>
        response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
}
