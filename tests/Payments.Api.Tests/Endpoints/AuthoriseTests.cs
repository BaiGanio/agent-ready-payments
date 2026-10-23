using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Payments.Api.Tests;

/// <summary>POST /payments/{id}/authorise. Red until step 4 adds the endpoint.</summary>
public sealed class AuthoriseTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Authorising_a_created_payment_is_200_and_Authorised()
    {
        var created = await CreatePaymentAsync();

        var response = await PostAsync($"/payments/{created.Id}/authorise");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<PaymentDto>(TestContext.Current.CancellationToken);

        Assert.Equal("Authorised", dto!.Status);
    }

    [Fact]
    public async Task An_authorised_payment_allows_capture_and_void()
    {
        var dto = await AuthorisedPaymentAsync();

        // The domain allows Void even though this lab has no void endpoint. CF-025.
        Assert.Equal(["Capture", "Void"], dto.AllowedActions);
    }

    [Fact]
    public async Task An_authorised_payment_can_capture_its_full_amount()
    {
        var dto = await AuthorisedPaymentAsync(100m);

        Assert.Equal(dto.AuthorisedAmount, dto.CapturableRemaining);
    }

    [Fact]
    public async Task Authorising_twice_is_409_illegal_transition()
    {
        var created = await CreatePaymentAsync();

        var first = await PostAsync($"/payments/{created.Id}/authorise");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var operationsAfterFirst = await CountRowsAsync("payment_operation");

        // A different key, so the 409 comes from the state machine and not from a replay.
        var second = await PostAsync($"/payments/{created.Id}/authorise");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("illegal_transition", await ErrorCodeAsync(second));
        Assert.Equal(operationsAfterFirst, await CountRowsAsync("payment_operation"));
    }
}
