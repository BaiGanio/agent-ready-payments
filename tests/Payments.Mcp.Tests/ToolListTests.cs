using System.Text.Json;
using ModelContextProtocol.Client;
using Xunit;

namespace Payments.Mcp.Tests;

/// <summary>
/// tools/list is the contract an agent reads. A renamed field or a lost
/// "required" entry is a breaking change for every agent using this server.
/// </summary>
public sealed class ToolListTests
{
    private static async Task<IReadOnlyDictionary<string, McpClientTool>> ListAsync(McpHarness h)
    {
        var tools = await h.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        return tools.ToDictionary(t => t.Name);
    }

    private static string[] RequiredOf(McpClientTool tool)
        => tool.JsonSchema.TryGetProperty("required", out var required)
            ? required.EnumerateArray().Select(e => e.GetString()!).Order().ToArray()
            : [];

    private static JsonElement PropertiesOf(McpClientTool tool)
        => tool.JsonSchema.GetProperty("properties");

    [Fact]
    public async Task Exactly_three_tools_are_listed()
    {
        await using var harness = await McpHarness.StartAsync();

        var tools = await ListAsync(harness);

        Assert.Equal(
            new[] { "get_payment", "list_payments", "refund_payment" },
            tools.Keys.Order().ToArray());
    }

    [Fact]
    public async Task Get_payment_requires_a_payment_id_string()
    {
        await using var harness = await McpHarness.StartAsync();

        var tool = (await ListAsync(harness))["get_payment"];
        var properties = PropertiesOf(tool);

        Assert.Equal(
            new[] { "payment_id" },
            properties.EnumerateObject().Select(p => p.Name).ToArray());

        var paymentId = properties.GetProperty("payment_id");
        Assert.Equal("string", paymentId.GetProperty("type").GetString());
        Assert.Equal("uuid", paymentId.GetProperty("format").GetString());

        Assert.Equal(new[] { "payment_id" }, RequiredOf(tool));
    }

    [Fact]
    public async Task Refund_requires_id_amount_and_currency_but_not_the_token()
    {
        await using var harness = await McpHarness.StartAsync();

        var tool = (await ListAsync(harness))["refund_payment"];
        var properties = PropertiesOf(tool);

        Assert.Equal(
            new[] { "amount", "currency", "payment_id" },
            RequiredOf(tool));

        // A float amount can arrive as 80.09999... from a client that uses doubles.
        Assert.Equal("string", properties.GetProperty("amount").GetProperty("type").GetString());

        Assert.True(properties.TryGetProperty("confirmation_token", out _),
            "confirmation_token must be offered, so the model can send the token back.");
        Assert.DoesNotContain("confirmation_token", RequiredOf(tool));
    }

    [Fact]
    public async Task Read_tools_are_read_only_and_refund_is_destructive()
    {
        await using var harness = await McpHarness.StartAsync();

        var tools = await ListAsync(harness);

        Assert.True(tools["get_payment"].ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.True(tools["list_payments"].ProtocolTool.Annotations?.ReadOnlyHint);

        var refund = tools["refund_payment"].ProtocolTool.Annotations;
        Assert.True(refund?.DestructiveHint);
        Assert.NotEqual(true, refund?.ReadOnlyHint);
    }

    // ---- the snapshot ----------------------------------------------------
    // Written by: you. Replace these three with the descriptions you wrote in
    // step 9, word for word. A changed sentence must change this test in the
    // same commit: the description is what the model reads, so it is code.

    private const string GetPaymentDescription =
        "Read one payment by its exact id. Returns the status, the currency and the " +
        "authorised, captured and refunded amounts. You must already have the payment id, " +
        "a UUID. If you only have a merchant, a date or a customer, call list_payments first. " +
        "An id that does not exist returns result: not_found, which is an answer, not a failure. " +
        "This tool only reads; it never changes a payment.";

    private const string ListPaymentsDescription =
        "List this merchant's payments, newest first, at most 20 at a time. The merchant is " +
        "fixed by the API key, so you cannot ask for another merchant's payments. Use status to " +
        "narrow the list. truncated: true means there are more payments than were returned; " +
        "narrow the list with status rather than asking for more. There is no paging: " +
        "there is no cursor, no page and no offset argument, so never invent one. " +
        "This tool only reads; it never changes a payment.";

    private const string RefundPaymentDescription =
        "Refund money from a payment back to the customer. This takes two calls. " +
        "The first call refunds nothing: it returns a confirmation_token and the exact refund " +
        "it would make, for a human to approve. Only after a human has said yes, call it a " +
        "second time with the same payment_id, amount and currency plus that confirmation_token; " +
        "that second call moves the money. Give amount as a plain decimal string such as \"80.00\", " +
        "and currency as a three-letter upper-case code such as \"EUR\". " +
        "Never call this twice for the same refund without a new approval.";

    [Fact]
    public async Task The_descriptions_match_the_snapshot()
    {
        await using var harness = await McpHarness.StartAsync();

        var tools = await ListAsync(harness);

        Assert.Equal(GetPaymentDescription, tools["get_payment"].Description);
        Assert.Equal(ListPaymentsDescription, tools["list_payments"].Description);
        Assert.Equal(RefundPaymentDescription, tools["refund_payment"].Description);
    }
}