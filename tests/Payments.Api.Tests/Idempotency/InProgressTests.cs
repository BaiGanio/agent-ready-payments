using System.Net;
using Payments.Api.Storage;
using Xunit;

namespace Payments.Api.Tests;

/// <summary>
/// A key whose first request is still running. The open key is arranged by hand with SQL, because
/// a test that waits for a race to happen is a test that passes by luck. The luck-based one is
/// <c>ConcurrencyTests</c>, on purpose and labelled. Red until step 12.
/// </summary>
public sealed class InProgressTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task An_open_key_is_409_idempotency_request_in_progress()
    {
        string key = NewKey();
        var body = new CreatePaymentRequest(SeededMerchant, 100m, "EUR");

        // The hash must match the request we are about to send. A different hash gives a 422
        // instead — that is the mismatch rule doing its job, not a bug.
        await OpenKeyAsync(key, RequestHash.Of("POST", "/payments", body));

        var response = await PostAsync("/payments", body, key);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("idempotency_request_in_progress", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task The_409_says_when_to_retry()
    {
        // A 409 with no Retry-After tells the client to guess. One second is the hint.
        string key = NewKey();
        var body = new CreatePaymentRequest(SeededMerchant, 100m, "EUR");

        await OpenKeyAsync(key, RequestHash.Of("POST", "/payments", body));

        var response = await PostAsync("/payments", body, key);

        Assert.Equal(TimeSpan.FromSeconds(1), response.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task An_open_key_runs_no_work()
    {
        // Create: no payment row. The claim comes before the work, so nothing ran.
        string createKey = NewKey();
        var create = new CreatePaymentRequest(SeededMerchant, 100m, "EUR");

        await OpenKeyAsync(createKey, RequestHash.Of("POST", "/payments", create));

        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync("/payments", create, createKey)).StatusCode);
        Assert.Equal(0, await CountRowsAsync("payment"));

        // Refund: no new operation row on a payment that already exists.
        var captured = await CapturedPaymentAsync(100m);

        long before = await CountRowsAsync("payment_operation");

        string refundKey = NewKey();
        var refund = new AmountRequest(40m, "EUR");
        string path = $"/payments/{captured.Id}/refund";

        await OpenKeyAsync(refundKey, RequestHash.Of("POST", path, refund));

        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(path, refund, refundKey)).StatusCode);
        Assert.Equal(before, await CountRowsAsync("payment_operation"));
    }

    // ----- helpers -----

    private static string NewKey() => Guid.NewGuid().ToString();

    /// <summary>
    /// An InProgress key row for the seeded merchant, written straight at Postgres.
    /// No store would ever leave a key open on purpose, so no store can arrange this.
    /// </summary>
    private Task OpenKeyAsync(string key, string requestHash) =>
        ExecuteSqlAsync(
            """
            insert into idempotency_key (merchant_id, key, request_hash, status)
            values (@Merchant, @Key, @Hash, 'InProgress')
            """,
            new { Merchant = SeededMerchant, Key = key, Hash = requestHash });
}
