using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Payments.Api.Tests;

/// <summary>POST /payments/{id}/refund. The endpoint this lab cares about. Red until step 4.</summary>
public sealed class RefundTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Refunding_a_captured_payment_is_200_and_Refunded()
    {
        var captured = await CapturedPaymentAsync(100m);

        var response = await RefundAsync(captured.Id, 100m);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await BodyAsync(response);

        Assert.Equal("Refunded", dto.Status);
        Assert.Empty(dto.AllowedActions);
    }

    [Fact]
    public async Task Refunding_part_is_200_and_PartiallyRefunded()
    {
        var captured = await CapturedPaymentAsync(100m);

        var dto = await BodyAsync(await RefundAsync(captured.Id, 60m));

        Assert.Equal("PartiallyRefunded", dto.Status);
        Assert.Equal(60m, dto.RefundedAmount);
        Assert.Equal(40m, dto.RefundableRemaining);
    }

    [Fact]
    public async Task A_refund_is_limited_by_what_was_captured()
    {
        // Authorised 100, captured 40. The 60 still on hold is not refundable.
        var captured = await CapturedPaymentAsync(100m, 40m);

        var response = await RefundAsync(captured.Id, 50m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("amount_exceeds_captured", await ErrorCodeAsync(response));

        var readBack = await GetPaymentAsync(captured.Id);

        Assert.Equal(40m, readBack.RefundableRemaining);
    }

    [Fact]
    public async Task Refunding_everything_captured_on_a_partial_capture_ends_Refunded()
    {
        var captured = await CapturedPaymentAsync(100m, 40m);

        var dto = await BodyAsync(await RefundAsync(captured.Id, 40m));

        Assert.Equal("Refunded", dto.Status);
        Assert.Equal(40m, dto.RefundedAmount);
    }

    [Fact]
    public async Task Refunding_an_authorised_payment_is_409()
    {
        // Nothing was captured, so there is nothing to give back.
        var authorised = await AuthorisedPaymentAsync(100m);

        var response = await RefundAsync(authorised.Id, 40m);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("illegal_transition", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Refunding_twice_over_the_total_is_422()
    {
        var captured = await CapturedPaymentAsync(100m);

        // Two different keys: the second is a real second refund, not a replay.
        var first = await RefundAsync(captured.Id, 60m);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await RefundAsync(captured.Id, 60m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        Assert.Equal("amount_exceeds_captured", await ErrorCodeAsync(second));
    }

    [Fact]
    public async Task Refunding_in_another_currency_is_422_currency_mismatch()
    {
        // Currency is checked before the amount, so an impossible amount still reports the currency.
        var captured = await CapturedPaymentAsync(100m, currency: "EUR");

        var response = await RefundAsync(captured.Id, 500m, "USD");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("currency_mismatch", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task The_error_body_is_problem_json_with_a_code()
    {
        var captured = await CapturedPaymentAsync(100m, 40m);

        var response = await RefundAsync(captured.Id, 50m);

        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);

        Assert.Equal("urn:payments-labs:error:amount_exceeds_captured", problem!.Type);
        Assert.Equal("amount_exceeds_captured", await ErrorCodeAsync(response));
        Assert.DoesNotContain("Exception", problem.Detail);
        Assert.DoesNotContain("   at ", problem.Detail);
    }

    // ----- helpers -----

    private Task<HttpResponseMessage> RefundAsync(Guid id, decimal amount, string currency = "EUR", string? key = null) =>
        PostAsync($"/payments/{id}/refund", new AmountRequest(amount, currency), key);

    private static async Task<PaymentDto> BodyAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<PaymentDto>(TestContext.Current.CancellationToken))!;

    private async Task<PaymentDto> GetPaymentAsync(Guid id) =>
        (await Client.GetFromJsonAsync<PaymentDto>($"/payments/{id}", TestContext.Current.CancellationToken))!;
}
