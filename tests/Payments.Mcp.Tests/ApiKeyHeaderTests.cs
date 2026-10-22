using Xunit;

namespace Payments.Mcp.Tests;

/// <summary>
/// The API key belongs to the MCP server, not to the model. It comes from the
/// environment, it rides on every request the server makes, and it must never
/// appear in anything the model reads back.
/// </summary>
public sealed class ApiKeyHeaderTests
{
    private const string GoodPaymentId = "0199c3f0-1a2b-7c3d-8e4f-5a6b7c8d9e0f";

    /// <summary>One payment body in the shape the API returns.</summary>
    private static object PaymentBody() => new
    {
        id = GoodPaymentId,
        merchantId = "0199c000-0000-7000-8000-000000000001",
        status = "Captured",
        currency = "EUR",
        authorisedAmount = 80.00m,
        capturedAmount = 80.00m,
        refundedAmount = 0.00m,
        capturableRemaining = 0.00m,
        refundableRemaining = 80.00m,
        allowedActions = new[] { "Refund" },
        declineReason = (string?)null,
        createdAt = "2026-10-21T10:00:00+00:00",
        updatedAt = "2026-10-21T10:00:00+00:00",
    };

    [Fact]
    public async Task Every_api_call_carries_the_configured_key()
    {
        await using var harness = await McpHarness.StartAsync();

        // One answer per call the three tools may make.
        harness.Api.Enqueue(System.Net.HttpStatusCode.OK, PaymentBody());
        harness.Api.Enqueue(System.Net.HttpStatusCode.OK, new { items = Array.Empty<object>(), truncated = false });
        harness.Api.Enqueue(System.Net.HttpStatusCode.OK, PaymentBody());

        await harness.CallAsync("get_payment", new { payment_id = GoodPaymentId });
        await harness.CallAsync("list_payments");
        await harness.CallAsync("refund_payment",
            new { payment_id = GoodPaymentId, amount = "80.00", currency = "EUR" });

        Assert.NotEmpty(harness.Api.Requests);

        foreach (var request in harness.Api.Requests)
        {
            Assert.True(
                request.Headers.TryGetValue("X-Api-Key", out var sent),
                $"{request.Method} {request.PathAndQuery} went out with no X-Api-Key header.");

            Assert.Equal(McpHarness.ApiKey, sent);
        }
    }

    [Fact]
    public async Task The_server_refuses_to_start_without_a_key()
    {
        // A missing key must stop the server at start-up. The other way round it shows up
        // as a 401 on the first tool call, long into a conversation, where nobody sees it.
        var failure = await Assert.ThrowsAnyAsync<Exception>(
            () => McpHarness.StartAsync(apiKey: ""));

        Assert.Contains("PAYMENTS_API_KEY", Describe(failure), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_key_never_reaches_the_model()
    {
        await using var harness = await McpHarness.StartAsync();

        // A hostile or careless API echoes the key in its error body. Passing that body
        // on to the model would hand it the credential.
        harness.Api.Enqueue(
            System.Net.HttpStatusCode.InternalServerError,
            new { detail = $"upstream failed for key {McpHarness.ApiKey}" });

        var (isError, body) = await harness.CallAsync("get_payment", new { payment_id = GoodPaymentId });

        Assert.NotEmpty(harness.Api.Requests);
        Assert.True(isError);
        Assert.Equal("tool_failed", body.GetProperty("error").GetString());
        Assert.DoesNotContain(McpHarness.ApiKey, body.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The message of the failure and of every inner one, so a wrapped failure still counts.</summary>
    private static string Describe(Exception error)
    {
        var text = new System.Text.StringBuilder();

        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            text.AppendLine(current.Message);
        }

        return text.ToString();
    }
}
