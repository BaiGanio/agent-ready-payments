using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Payments.Api.Tests;

/// <summary>GET /payments/{id} — what survives the trip through Postgres and back.</summary>
public sealed class GetPaymentTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task A_created_payment_can_be_read_back()
    {
        var created = await CreatePaymentAsync(amount: 123.45m);

        var response = await Client.GetAsync($"/payments/{created.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var read = await response.Content.ReadFromJsonAsync<PaymentDto>(TestContext.Current.CancellationToken);

        Assert.Equal(created.Id, read!.Id);
        Assert.Equal(created.MerchantId, read.MerchantId);
        Assert.Equal(created.Status, read.Status);
        Assert.Equal(created.Currency, read.Currency);
        Assert.Equal(created.AuthorisedAmount, read.AuthorisedAmount);
        Assert.Equal(created.CapturedAmount, read.CapturedAmount);
        Assert.Equal(created.RefundedAmount, read.RefundedAmount);
        Assert.Equal(created.CapturableRemaining, read.CapturableRemaining);
        Assert.Equal(created.RefundableRemaining, read.RefundableRemaining);
        Assert.Equal(created.AllowedActions, read.AllowedActions);
        Assert.Equal(created.DeclineReason, read.DeclineReason);
        Assert.Equal(created.CreatedAt, read.CreatedAt, TimeSpan.FromMilliseconds(1));

        // CF-019. The create response and the read must answer updatedAt from the same
        // clock. InsertAsync writes updated_at = created_at, so on a brand new payment
        // both endpoints owe the same value, and that value is the injected clock's.
        Assert.Equal(created.UpdatedAt, read.UpdatedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(created.CreatedAt, created.UpdatedAt);
    }

    [Fact]
    public async Task An_unknown_id_is_404_payment_not_found()
    {
        var response = await Client.GetAsync($"/payments/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("payment_not_found", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task A_malformed_id_is_404_not_500()
    {
        var response = await Client.GetAsync($"/payments/not-a-guid", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_read_body_is_application_json()
    {
        var dto = await CreatePaymentAsync();

        var response = await Client.GetAsync($"/payments/{dto.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        response = await Client.GetAsync($"/payments/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}

