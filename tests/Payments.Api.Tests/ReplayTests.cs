using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Payments.Api.Tests;

/// <summary>
/// The same key twice on POST /payments. A repeat is not an error: it is the first answer again,
/// byte for byte, with a header that says so. Red until step 10.
/// </summary>
public sealed class ReplayTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task The_same_key_twice_creates_one_payment()
    {
        string key = NewKey();

        var first = await CreateAsync(key);
        var second = await CreateAsync(key);

        Assert.Equal((await BodyAsync(first)).Id, (await BodyAsync(second)).Id);
        Assert.Equal(1, await CountRowsAsync("payment"));
    }

    [Fact]
    public async Task A_replay_returns_the_same_status_and_body()
    {
        string key = NewKey();

        var first = await CreateAsync(key);
        var second = await CreateAsync(key);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        // Strings, not DTOs. Two DTOs can be equal while the bytes differ, and the rule is the bytes.
        Assert.Equal(await TextAsync(first), await TextAsync(second));
    }

    [Fact]
    public async Task A_replay_carries_the_Idempotent_Replayed_header()
    {
        string key = NewKey();

        var first = await CreateAsync(key);
        var second = await CreateAsync(key);

        // The first answer did the work, so it must not claim to be a replay.
        Assert.False(first.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal("true", second.Headers.GetValues("Idempotent-Replayed").Single());
    }

    [Fact]
    public async Task A_replay_keeps_the_location_header()
    {
        string key = NewKey();

        var first = await CreateAsync(key);
        var second = await CreateAsync(key);

        Assert.NotNull(first.Headers.Location);
        Assert.Equal(first.Headers.Location, second.Headers.Location);
    }

    [Fact]
    public async Task Two_different_keys_create_two_payments()
    {
        // The same body is not the thing that makes a repeat. The key is.
        var first = await BodyAsync(await CreateAsync(NewKey()));
        var second = await BodyAsync(await CreateAsync(NewKey()));

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, await CountRowsAsync("payment"));
    }

    [Fact]
    public async Task The_same_key_from_two_merchants_creates_two_payments()
    {
        // The key is scoped to the merchant, so two merchants never collide on a plain string like "K".
        string key = "K";

        var other = await ClientWith(["payments:read", "payments:write"], merchant: OtherMerchant);

        var mine = await PostAsync("/payments", new CreatePaymentRequest(SeededMerchant, 100m, "EUR"), key);
        var theirs = await PostAsync("/payments", new CreatePaymentRequest(OtherMerchant, 100m, "EUR"), key, other);

        Assert.Equal(HttpStatusCode.Created, mine.StatusCode);
        Assert.Equal(HttpStatusCode.Created, theirs.StatusCode);

        Assert.NotEqual((await BodyAsync(mine)).Id, (await BodyAsync(theirs)).Id);
        Assert.Equal(2, await CountRowsAsync("payment"));
    }

    [Fact]
    public async Task A_replay_shows_the_original_answer_not_the_current_state()
    {
        // The one people get wrong. A replay is a stored answer, not a fresh read.
        string key = NewKey();

        var created = await BodyAsync(await CreateAsync(key));

        var authorise = await PostAsync($"/payments/{created.Id}/authorise");

        Assert.Equal(HttpStatusCode.OK, authorise.StatusCode);
        Assert.Equal("Authorised", (await BodyAsync(authorise)).Status);

        var replay = await CreateAsync(key);

        Assert.Equal("Created", (await BodyAsync(replay)).Status);
    }

    // ----- helpers -----

    private static string NewKey() => Guid.NewGuid().ToString();

    private Task<HttpResponseMessage> CreateAsync(string key, decimal amount = 100m) =>
        PostAsync("/payments", new CreatePaymentRequest(SeededMerchant, amount, "EUR"), key);

    private static async Task<PaymentDto> BodyAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<PaymentDto>(TestContext.Current.CancellationToken))!;

    private static Task<string> TextAsync(HttpResponseMessage response) =>
        response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
}
