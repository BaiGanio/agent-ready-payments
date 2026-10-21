using System.Net;
using Payments.Api.Auth;
using Xunit;

namespace Payments.Api.Tests;

/// <summary>X-Api-Key: "who are you?". Red until step 4.</summary>
public sealed class ApiKeyTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task No_key_is_401_api_key_required(string? header)
    {
        var payment = await CreatePaymentAsync();

        var response = await GetWithKeyAsync($"/payments/{payment.Id}", header);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("api_key_required", await ErrorCodeAsync(response));
        Assert.NotEmpty(response.Headers.WwwAuthenticate);
    }

    [Fact]
    public async Task An_unknown_key_is_401_api_key_invalid()
    {
        var payment = await CreatePaymentAsync();

        var response = await GetWithKeyAsync($"/payments/{payment.Id}", Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("api_key_invalid", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task A_revoked_key_is_401_api_key_invalid()
    {
        var payment = await CreatePaymentAsync();
        var raw = await InsertKeyAsync(["payments:read"]);

        await ExecuteSqlAsync(
            "update api_key set revoked_at = now() where key_hash = @Hash",
            new { Hash = ApiKeyHash.Of(raw) });

        var response = await GetWithKeyAsync($"/payments/{payment.Id}", raw);

        // The same code as a key that never existed: a caller cannot tell the two apart.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("api_key_invalid", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Health_needs_no_key()
    {
        var response = await GetWithKeyAsync("/health", header: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Only_the_hash_is_stored()
    {
        var raw = await InsertKeyAsync(["payments:read"]);

        Assert.Equal(0, await QuerySqlAsync<long>(
            "select count(*) from api_key where key_hash = @Raw", new { Raw = raw }));
        Assert.Equal(1, await QuerySqlAsync<long>(
            "select count(*) from api_key where key_hash = @Hash", new { Hash = ApiKeyHash.Of(raw) }));
    }

    /// <summary>A GET from a client with no default key. <paramref name="header"/> null sends no header at all.</summary>
    private async Task<HttpResponseMessage> GetWithKeyAsync(string path, string? header)
    {
        using var client = Fixture.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        if (header is not null)
            request.Headers.TryAddWithoutValidation("X-Api-Key", header);

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
