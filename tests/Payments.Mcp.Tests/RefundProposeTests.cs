using System.Net;
using System.Text.Json;
using Xunit;

namespace Payments.Mcp.Tests;

/// <summary>
/// Phase 1 of the refund: the proposal. The rule they all share is that phase 1
/// moves no money. It reads the payment, says exactly what it would refund, and
/// hands back a token for a human to approve.
/// </summary>
public sealed class RefundProposeTests
{
    private const string GoodPaymentId = "0199c3f0-1a2b-7c3d-8e4f-5a6b7c8d9e0f";

    /// <summary>A captured payment, in the shape the API returns.</summary>
    private static object PaymentBody(decimal refundable = 80.00m, string currency = "EUR") => new
    {
        id = GoodPaymentId,
        merchantId = "0199c000-0000-7000-8000-000000000001",
        status = "Captured",
        currency,
        authorisedAmount = 80.00m,
        capturedAmount = 80.00m,
        refundedAmount = 80.00m - refundable,
        capturableRemaining = 0.00m,
        refundableRemaining = refundable,
        allowedActions = new[] { "Refund" },
        declineReason = (string?)null,
        createdAt = "2026-10-21T10:00:00+00:00",
        updatedAt = "2026-10-21T10:00:00+00:00",
    };

    /// <summary>A ProblemDetails body in the shape the API returns, with its "code".</summary>
    private static object Problem(int status, string code, string detail) => new
    {
        type = $"urn:payments-labs:error:{code}",
        title = code,
        status,
        detail,
        instance = $"/payments/{GoodPaymentId}",
        code,
    };

    /// <summary>The first call: the three arguments, and no token.</summary>
    private static Task<(bool IsError, JsonElement Body)> ProposeAsync(
        McpHarness harness, string amount = "80.00", string currency = "EUR")
        => harness.CallAsync("refund_payment", new
        {
            payment_id = GoodPaymentId,
            amount,
            currency,
        });

    [Fact]
    public async Task A_proposal_posts_nothing()
    {
        await using var harness = await McpHarness.StartAsync();

        harness.Api.Enqueue(HttpStatusCode.OK, PaymentBody());

        await ProposeAsync(harness);

        // How the money is lost: the model calls the tool once, "to see what would
        // happen", and the customer is paid. A proposal that refunds is not a proposal.
        var request = Assert.Single(harness.Api.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.DoesNotContain(harness.Api.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task The_proposal_names_the_amount_currency_and_payment()
    {
        await using var harness = await McpHarness.StartAsync();

        harness.Api.Enqueue(HttpStatusCode.OK, PaymentBody());

        var (isError, body) = await ProposeAsync(harness);

        Assert.False(isError, body.ToString());
        Assert.Equal("confirmation_required", body.GetProperty("result").GetString());

        // How the money is lost: the human approves a vague "refund this payment?"
        // and the agent sends a different amount. What they read must be what is sent.
        var proposal = body.GetProperty("proposal").GetString() ?? string.Empty;
        Assert.Contains("80.00", proposal, StringComparison.Ordinal);
        Assert.Contains("EUR", proposal, StringComparison.Ordinal);
        Assert.Contains(GoodPaymentId, proposal, StringComparison.OrdinalIgnoreCase);

        var token = body.GetProperty("confirmation_token").GetString();
        Assert.StartsWith("rf_", token ?? string.Empty, StringComparison.Ordinal);

        Assert.True(DateTimeOffset.TryParse(
            body.GetProperty("expires_at").GetString(), out _));

        // And the next step must be a person, not another tool call.
        var next = body.GetProperty("next").GetString() ?? string.Empty;
        Assert.Contains("human", next, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task More_than_refundable_is_refused_without_a_token()
    {
        await using var harness = await McpHarness.StartAsync();

        harness.Api.Enqueue(HttpStatusCode.OK, PaymentBody(refundable: 30.00m));

        var (isError, body) = await ProposeAsync(harness, amount: "80.00");

        // How the money is lost: a token for an impossible refund is a loaded gun.
        // The human says yes to 80.00, and whatever the API then does, nobody checked.
        Assert.True(isError, body.ToString());
        Assert.Equal("amount_exceeds_captured", body.GetProperty("error").GetString());
        Assert.Contains("30.00", body.GetProperty("message").GetString() ?? string.Empty,
            StringComparison.Ordinal);
        Assert.False(body.TryGetProperty("confirmation_token", out _));
    }

    [Fact]
    public async Task Another_currency_is_refused_without_a_token()
    {
        await using var harness = await McpHarness.StartAsync();

        harness.Api.Enqueue(HttpStatusCode.OK, PaymentBody(currency: "EUR"));

        var (isError, body) = await ProposeAsync(harness, currency: "USD");

        // How the money is lost: 80 USD is not 80 EUR. A token that holds the wrong
        // currency turns a rate into a loss, and the human read "80" and said yes.
        Assert.True(isError, body.ToString());
        Assert.Equal("currency_mismatch", body.GetProperty("error").GetString());
        Assert.False(body.TryGetProperty("confirmation_token", out _));
    }

    [Fact]
    public async Task A_missing_payment_is_refused_without_a_token()
    {
        await using var harness = await McpHarness.StartAsync();

        harness.Api.Enqueue(
            HttpStatusCode.NotFound,
            Problem(404, "payment_not_found", "There is no payment with that id."));

        var (isError, body) = await ProposeAsync(harness);

        // How the money is lost: for a read, a miss is a fact. For a refund it is a
        // wrong id, so the agent must stop, not propose a refund on a payment it
        // cannot see and let a human approve a guess.
        Assert.True(isError, body.ToString());
        Assert.Equal("payment_not_found", body.GetProperty("error").GetString());
        Assert.False(body.TryGetProperty("confirmation_token", out _));
    }
}
