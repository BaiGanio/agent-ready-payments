using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Payments.Api.Tests;

/// <summary>POST /payments/{id}/capture. Red until step 4 adds the endpoint.</summary>
public sealed class CaptureTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Capturing_the_full_amount_is_200_and_Captured()
    {
        var authorised = await AuthorisedPaymentAsync(100m);

        var response = await CaptureAsync(authorised.Id, 100m);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await BodyAsync(response);

        Assert.Equal("Captured", dto.Status);
        Assert.Equal(dto.AuthorisedAmount, dto.CapturedAmount);
    }

    [Fact]
    public async Task Capturing_part_is_200_and_PartiallyCaptured()
    {
        var authorised = await AuthorisedPaymentAsync(100m);

        var dto = await BodyAsync(await CaptureAsync(authorised.Id, 40m));

        Assert.Equal("PartiallyCaptured", dto.Status);
        Assert.Equal(40m, dto.CapturedAmount);
        Assert.Equal(60m, dto.CapturableRemaining);
    }

    [Fact]
    public async Task Capturing_exactly_what_is_left_is_allowed()
    {
        // The limit is inclusive: 40 then 60 of 100 is fine.
        var authorised = await AuthorisedPaymentAsync(100m);

        await CaptureAsync(authorised.Id, 40m);

        var response = await CaptureAsync(authorised.Id, 60m);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await BodyAsync(response);

        Assert.Equal("Captured", dto.Status);
        Assert.Equal(100m, dto.CapturedAmount);
    }

    [Fact]
    public async Task Capturing_more_than_authorised_is_422_amount_exceeds_authorised()
    {
        var authorised = await AuthorisedPaymentAsync(100m);

        var response = await CaptureAsync(authorised.Id, 101m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("amount_exceeds_authorised", await ErrorCodeAsync(response));

        var readBack = await GetPaymentAsync(authorised.Id);

        Assert.Equal(0m, readBack.CapturedAmount);
    }

    [Fact]
    public async Task Capturing_a_payment_that_was_never_authorised_is_409()
    {
        // State is checked before the amount, so this is a 409 and not a 422 about money.
        var created = await CreatePaymentAsync(100m);

        var response = await CaptureAsync(created.Id, 40m);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("illegal_transition", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Capturing_in_another_currency_is_422_currency_mismatch()
    {
        var authorised = await AuthorisedPaymentAsync(100m, "EUR");

        var response = await CaptureAsync(authorised.Id, 40m, "USD");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("currency_mismatch", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task A_refused_capture_writes_no_operation_row()
    {
        var authorised = await AuthorisedPaymentAsync(100m);

        var before = await CountRowsAsync("payment_operation");

        var tooMuch = await CaptureAsync(authorised.Id, 101m);
        var wrongCurrency = await CaptureAsync(authorised.Id, 40m, "USD");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooMuch.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongCurrency.StatusCode);
        Assert.Equal(before, await CountRowsAsync("payment_operation"));
    }

    [Fact]
    public async Task Two_captures_add_up()
    {
        var authorised = await AuthorisedPaymentAsync(100m);

        var before = await CountRowsAsync("payment_operation");

        // Two different keys: two real captures, not a replay.
        await CaptureAsync(authorised.Id, 30m);

        var dto = await BodyAsync(await CaptureAsync(authorised.Id, 30m));

        Assert.Equal(60m, dto.CapturedAmount);
        Assert.Equal(before + 2, await CountRowsAsync("payment_operation"));
    }

    // ----- helpers -----

    private Task<HttpResponseMessage> CaptureAsync(Guid id, decimal amount, string currency = "EUR", string? key = null) =>
        PostAsync($"/payments/{id}/capture", new AmountRequest(amount, currency), key);

    private static async Task<PaymentDto> BodyAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<PaymentDto>(TestContext.Current.CancellationToken))!;

    private async Task<PaymentDto> GetPaymentAsync(Guid id) =>
        (await Client.GetFromJsonAsync<PaymentDto>($"/payments/{id}", TestContext.Current.CancellationToken))!;
}
