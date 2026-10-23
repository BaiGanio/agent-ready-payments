using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Payments.Api.Tests;

/// <summary>
/// The Idempotency-Key header itself: present, not blank, not too long.
/// This check runs before anything else, so a bad header never reaches the database.
/// Red until step 10.
/// </summary>
public sealed class KeyHeaderTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Theory]
    [InlineData("create")]
    [InlineData("authorise")]
    [InlineData("capture")]
    [InlineData("refund")]
    public async Task A_missing_key_is_400_idempotency_key_required(string endpoint)
    {
        // Every payment exists already, so a 400 here cannot be a 404 wearing the wrong number.
        var (path, body) = await TargetAsync(endpoint);

        var response = await SendAsync(path, body, key: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("idempotency_key_required", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task A_blank_key_is_400_idempotency_key_required()
    {
        // A header made of spaces is not a key. Trim first, then decide.
        var response = await SendAsync("/payments", NewPayment(), "   ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("idempotency_key_required", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task A_key_longer_than_255_is_400_idempotency_key_invalid()
    {
        // 256 characters. The column stops at 255, so the edge refuses it before the insert does.
        var response = await SendAsync("/payments", NewPayment(), new string('k', 256));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("idempotency_key_invalid", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task A_key_of_exactly_255_is_accepted()
    {
        // The limit is inclusive. 255 is a legal key, not a long one.
        var response = await SendAsync("/payments", NewPayment(), new string('k', 255));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task A_refused_key_writes_nothing()
    {
        var refused = new[]
        {
            await SendAsync("/payments", NewPayment(), key: null),
            await SendAsync("/payments", NewPayment(), "   "),
            await SendAsync("/payments", NewPayment(), new string('k', 256))
        };

        Assert.All(refused, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));

        Assert.Equal(0, await CountRowsAsync("payment"));
        Assert.Equal(0, await CountRowsAsync("idempotency_key"));
    }

    [Fact]
    public async Task A_GET_needs_no_key()
    {
        // A read changes nothing, so it needs no key. Only money-moving POSTs do.
        var created = await CreatePaymentAsync();

        var response = await Client.GetAsync($"/payments/{created.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ----- helpers -----

    private CreatePaymentRequest NewPayment(decimal amount = 100m) => new(SeededMerchant, amount, "EUR");

    /// <summary>
    /// The path and body for one of the four POSTs, with the payment already in the right state.
    /// The arranging calls send their own keys, so only the call under test is header-less.
    /// </summary>
    private async Task<(string Path, object? Body)> TargetAsync(string endpoint)
    {
        switch (endpoint)
        {
            case "create":
                return ("/payments", NewPayment());

            case "authorise":
                var created = await CreatePaymentAsync();
                return ($"/payments/{created.Id}/authorise", null);

            case "capture":
                var authorised = await AuthorisedPaymentAsync();
                return ($"/payments/{authorised.Id}/capture", new AmountRequest(100m, "EUR"));

            case "refund":
                var captured = await CapturedPaymentAsync();
                return ($"/payments/{captured.Id}/refund", new AmountRequest(100m, "EUR"));

            default:
                throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, "Unknown endpoint.");
        }
    }

    /// <summary>
    /// Posts with the exact header this test wants, or none at all. The ApiTestBase helper
    /// always adds a fresh key, so these tests build the request by hand.
    /// TryAddWithoutValidation keeps a blank value blank; Add would trim it away.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(string path, object? body, string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);

        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType());

        if (key is not null)
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        return await Client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
