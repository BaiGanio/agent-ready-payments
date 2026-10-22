using System.Text.Json;
using Xunit;

namespace Payments.Mcp.Tests;

/// <summary>
/// The SDK drops an argument the tool does not declare, and it does not enforce
/// enum, minimum or maximum from its own schema. The schema is a promise to the
/// model, not a check. These tests prove the server keeps the promise anyway.
/// A refused call must never reach the API, so every test also asserts zero requests.
/// </summary>
public sealed class ArgumentFenceTests
{
    private const string GoodPaymentId = "0199c3f0-1a2b-7c3d-8e4f-5a6b7c8d9e0f";

    /// <summary>Calls the tool, then asserts the error code and that no request went out.</summary>
    private static async Task<string> RefusedAsync(
        McpHarness harness, string tool, object arguments, string expectedCode)
    {
        var (isError, body) = await harness.CallAsync(tool, arguments);

        Assert.True(isError, $"{tool} should have refused these arguments.");
        Assert.Equal(expectedCode, body.GetProperty("error").GetString());
        Assert.Empty(harness.Api.Requests);

        return body.GetProperty("message").GetString() ?? string.Empty;
    }

    [Fact]
    public async Task An_unknown_argument_is_refused_by_name()
    {
        await using var harness = await McpHarness.StartAsync();

        var message = await RefusedAsync(
            harness,
            "get_payment",
            new { payment_id = GoodPaymentId, merchant = "M1 Hotel" },
            "unknown_argument");

        // Naming it is the point: the model must learn the argument does not exist.
        Assert.Contains("merchant", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_invented_cursor_is_refused()
    {
        await using var harness = await McpHarness.StartAsync();

        // There is no paging. Answering with page 1 would let the model believe
        // it is walking pages that do not exist.
        var message = await RefusedAsync(
            harness,
            "list_payments",
            new { cursor = "abc" },
            "unknown_argument");

        Assert.Contains("cursor", message, StringComparison.Ordinal);
    }


    [Theory]
    [InlineData("42")]
    [InlineData("order-1234")]
    public async Task A_payment_id_that_is_not_a_uuid_is_refused(string paymentId)
    {
        await using var harness = await McpHarness.StartAsync();

        var message = await RefusedAsync(
            harness,
            "get_payment",
            new { payment_id = paymentId },
            "invalid_payment_id");

        // Not the SDK's "An error occurred invoking 'get_payment'", which teaches nothing.
        Assert.Contains("UUID", message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    // A bad amount, with a good currency.
    [InlineData("80,00", "EUR", "invalid_amount")]
    [InlineData("80,00 euro?", "EUR", "invalid_amount")]
    [InlineData("1e3", "EUR", "invalid_amount")]
    [InlineData("-5", "EUR", "invalid_amount")]
    [InlineData(" 80.00", "EUR", "invalid_amount")]
    [InlineData("80.005", "EUR", "invalid_amount")]
    [InlineData("", "EUR", "invalid_amount")]
    // A good amount, with a bad currency.
    [InlineData("80.00", "eur", "invalid_currency")]
    [InlineData("80.00", "EURO", "invalid_currency")]
    [InlineData("80.00", "\u20ac", "invalid_currency")]
    public async Task Malformed_money_is_refused(string amount, string currency, string expectedCode)
    {
        await using var harness = await McpHarness.StartAsync();

        // Refuse, never repair. A trimmed or re-punctuated amount is a guess,
        // and a guess on a refund is money sent to the wrong place.
        await RefusedAsync(
            harness,
            "refund_payment",
            new { payment_id = GoodPaymentId, amount, currency },
            expectedCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.00")]
    public async Task A_zero_amount_is_refused(string amount)
    {
        await using var harness = await McpHarness.StartAsync();

        // Both match the pattern. The "> 0" check is a separate step.
        await RefusedAsync(
            harness,
            "refund_payment",
            new { payment_id = GoodPaymentId, amount, currency = "EUR" },
            "invalid_amount");
    }

    [Theory]
    [InlineData("captured")]
    [InlineData("Settled")]
    public async Task A_status_outside_the_enum_is_refused(string status)
    {
        await using var harness = await McpHarness.StartAsync();

        // The schema lists the enum. The SDK does not enforce it, so the server must.
        await RefusedAsync(harness, "list_payments", new { status }, "invalid_status");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    public async Task A_limit_outside_1_to_20_is_refused(int limit)
    {
        await using var harness = await McpHarness.StartAsync();

        // Same reason: [Range(1, 20)] reaches the schema and is never checked.
        await RefusedAsync(harness, "list_payments", new { limit }, "invalid_limit");
    }
}