using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Payments.Api.Tests;

/// <summary>
/// Pins what the API does today with a bad body: 400, the field named, nothing written.
/// Green at once on purpose. Step 10 puts a header check in front of this, and these
/// tests are what notice if the old behaviour breaks.
/// </summary>
public sealed class ValidationTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task A_missing_currency_is_400_naming_the_field()
    {
        var response = await Client.PostAsJsonAsync(
            "/payments",
            new { merchantId = SeededMerchant, amount = 100m },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Currency", await FieldNamesAsync(response), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_three_letter_lower_case_currency_is_400()
    {
        var response = await PostBodyAsync(100m, "eur");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Currency", await FieldNamesAsync(response), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_four_letter_currency_is_400()
    {
        var response = await PostBodyAsync(100m, "EURO");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Currency", await FieldNamesAsync(response), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_zero_amount_is_400()
    {
        var response = await PostBodyAsync(0m, "EUR");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Amount", await FieldNamesAsync(response), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_negative_amount_is_400()
    {
        var response = await PostBodyAsync(-5m, "EUR");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Amount", await FieldNamesAsync(response), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_empty_body_is_400()
    {
        var response = await PostRawAsync("");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Malformed_json_is_400()
    {
        var response = await PostRawAsync("{\"amount\":");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_validation_failure_writes_no_row()
    {
        Assert.Equal(0, await CountRowsAsync("payment"));

        var response = await PostBodyAsync(-5m, "eur");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountRowsAsync("payment"));
    }

    // ----- helpers -----

    /// <summary>Posts a well-formed body with a value the validator should refuse.</summary>
    private Task<HttpResponseMessage> PostBodyAsync(decimal amount, string currency) =>
        Client.PostAsJsonAsync(
            "/payments",
            new CreatePaymentRequest(SeededMerchant, amount, currency),
            TestContext.Current.CancellationToken);

    /// <summary>Posts a string the JSON binder has to reject. PostAsJsonAsync cannot send broken JSON.</summary>
    private Task<HttpResponseMessage> PostRawAsync(string json) =>
        Client.PostAsync(
            "/payments",
            new StringContent(json, Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

    /// <summary>The field names in the "errors" dictionary of a validation problem.</summary>
    private static async Task<IEnumerable<string>> FieldNamesAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(
            TestContext.Current.CancellationToken);

        return problem!.Errors.Keys;
    }
}
