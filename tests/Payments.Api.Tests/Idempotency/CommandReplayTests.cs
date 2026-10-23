using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Payments.Api.Tests;

/// <summary>
/// The same key twice on a command. Create was the easy case — a new row. A command changes a row
/// that already exists, so the same request twice would move the money twice. Red until step 12.
/// </summary>
public sealed class CommandReplayTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Refunding_twice_with_one_key_refunds_once()
    {
        // The case that really costs money, and the one an agent hits in lab 08.
        var captured = await CapturedPaymentAsync(100m);
        string key = NewKey();

        var first = await RefundAsync(captured.Id, 40m, key);
        var second = await RefundAsync(captured.Id, 40m, key);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        Assert.Equal(40.00m, (await BodyAsync(second)).RefundedAmount);
        Assert.Equal(1, await OperationCountAsync(captured.Id, "Refund"));
    }

    [Fact]
    public async Task Capturing_twice_with_one_key_captures_once()
    {
        var authorised = await AuthorisedPaymentAsync(100m);
        string key = NewKey();

        var first = await PostAsync($"/payments/{authorised.Id}/capture", new AmountRequest(40m, "EUR"), key);
        var second = await PostAsync($"/payments/{authorised.Id}/capture", new AmountRequest(40m, "EUR"), key);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        Assert.Equal(40.00m, (await BodyAsync(second)).CapturedAmount);
        Assert.Equal(1, await OperationCountAsync(authorised.Id, "Capture"));
    }

    [Fact]
    public async Task Authorising_twice_with_one_key_is_200_twice()
    {
        // Compare with AuthoriseTests.Authorising_twice_is_409_illegal_transition: that one sends
        // two different keys, so the second call is a real second request and the domain refuses it.
        var created = await CreatePaymentAsync();
        string key = NewKey();

        var first = await PostAsync($"/payments/{created.Id}/authorise", key: key);
        var second = await PostAsync($"/payments/{created.Id}/authorise", key: key);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        Assert.Equal("true", second.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(1, await OperationCountAsync(created.Id, "Authorise"));
    }

    [Fact]
    public async Task A_refused_command_is_stored_and_replayed()
    {
        // A 422 from the state machine is a final business answer, so it is stored and replayed.
        // Only a crash is released. That split is the rule of this lab.
        var captured = await CapturedPaymentAsync(100m);
        string key = NewKey();

        var first = await RefundAsync(captured.Id, 500m, key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, first.StatusCode);

        string firstBody = await TextAsync(first);

        var second = await RefundAsync(captured.Id, 500m, key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        Assert.Equal("true", second.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(firstBody, await TextAsync(second));
    }

    [Fact]
    public async Task A_404_is_not_stored()
    {
        // The payment is loaded before the claim, so an unknown id never writes a key row.
        // A 404 may not be true tomorrow: the client must be free to send the same key again.
        var response = await RefundAsync(Guid.NewGuid(), 40m, NewKey());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("payment_not_found", await ErrorCodeAsync(response));
        Assert.Equal(0, await CountRowsAsync("idempotency_key"));
    }

    // ----- helpers -----

    private static string NewKey() => Guid.NewGuid().ToString();

    private Task<HttpResponseMessage> RefundAsync(Guid id, decimal amount, string key) =>
        PostAsync($"/payments/{id}/refund", new AmountRequest(amount, "EUR"), key);

    private static async Task<PaymentDto> BodyAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<PaymentDto>(TestContext.Current.CancellationToken))!;

    private static Task<string> TextAsync(HttpResponseMessage response) =>
        response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

    /// <summary>Counts one kind of operation row, straight from Postgres. The money, not the answer.</summary>
    private Task<long> OperationCountAsync(Guid paymentId, string kind) =>
        QuerySqlAsync<long>(
            "select count(*) from payment_operation where payment_id = @Id and kind = @Kind",
            new { Id = paymentId, Kind = kind });
}
