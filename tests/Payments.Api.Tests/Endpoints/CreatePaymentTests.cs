using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Payments.Api.Tests;


public sealed class CreatePaymentTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Creating_a_payment_is_201_with_a_location_header()
    {
        // PostAsync, not PostAsJsonAsync: every POST needs an Idempotency-Key from step 10 on,
        // and the helper sends a fresh one.
        var response = await PostAsync("/payments", new CreatePaymentRequest(SeededMerchant, 100m, "EUR"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<PaymentDto>(TestContext.Current.CancellationToken);

        Assert.Equal($"/payments/{dto!.Id}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task A_new_payment_starts_in_status_Created()
    {
        var dto = await CreatePaymentAsync();

        Assert.Equal("Created", dto.Status);
        Assert.Equal(0m, dto.CapturedAmount);
        Assert.Equal(0m, dto.RefundedAmount);
    }

    [Fact]
    public async Task A_new_payment_allows_authorise_and_decline()
    {
        var dto = await CreatePaymentAsync();

        Assert.Equal(["Authorise", "Decline"], dto.AllowedActions);
    }

    [Fact]
    public async Task A_new_payment_has_no_remaining_amounts_yet()
    {
        var dto = await CreatePaymentAsync();

        Assert.Equal(0m, dto.CapturableRemaining);
        Assert.Equal(0m, dto.RefundableRemaining);
    }

    [Fact]
    public async Task The_server_chooses_the_id()
    {
        var dto1 = await CreatePaymentAsync();
        var dto2 = await CreatePaymentAsync();

        Assert.NotEqual(dto1.Id, dto2.Id);
    }

    [Fact]
    public async Task An_unknown_merchant_is_404_merchant_not_found()
    {
        var response = await PostAsync("/payments", new CreatePaymentRequest(Guid.NewGuid(), 100m, "EUR"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("merchant_not_found", await ErrorCodeAsync(response));
        Assert.Equal(0, await CountRowsAsync("payment"));
    }
}