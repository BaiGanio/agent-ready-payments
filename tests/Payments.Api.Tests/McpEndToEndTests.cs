using System.Text.Json;
using Xunit;

namespace Payments.Api.Tests;

/// <summary>
/// The MCP server against the real API and the real Postgres. The stub tests prove the
/// server keeps its promises; these prove the API it believes in is the API that exists.
/// Every assertion that matters here is a row count, not a tool's say-so.
/// </summary>
public sealed class McpEndToEndTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task The_double_charge_story_refunds_once()
    {
        // How an agent loses money if this breaks: the human approves one refund, the
        // agent calls confirm twice, and the customer is paid 80.00 EUR twice.
        var first = await CapturedPaymentAsync(80m);
        var second = await CapturedPaymentAsync(80m);

        string agentKey = await InsertKeyAsync(["payments:read", "payments:refund"], kind: "agent");
        await using var mcp = await McpEndToEndHarness.StartAsync(Fixture, agentKey);

        var (listFailed, list) = await mcp.CallAsync("list_payments", new { status = "Captured" });

        Assert.False(listFailed);
        Assert.Equal("ok", list.GetProperty("result").GetString());

        // Newest first, and both of them: this is the list the agent would show a human.
        Guid[] listed = [.. list.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid())];

        Assert.Equal([second.Id, first.Id], listed);

        var (proposeFailed, proposal) = await mcp.CallAsync(
            "refund_payment", new { payment_id = first.Id.ToString("D"), amount = "80.00", currency = "EUR" });

        Assert.False(proposeFailed);
        Assert.Equal("confirmation_required", proposal.GetProperty("result").GetString());

        string token = proposal.GetProperty("confirmation_token").GetString()!;

        // Nothing is refunded by a proposal. A token is a question, not a payment.
        Assert.Equal(0, await RefundRowsAsync(first.Id));

        var (confirmFailed, refunded) = await mcp.CallAsync(
            "refund_payment",
            new { payment_id = first.Id.ToString("D"), amount = "80.00", currency = "EUR", confirmation_token = token });

        Assert.False(confirmFailed);
        Assert.Equal("refunded", refunded.GetProperty("result").GetString());

        var (replayFailed, replay) = await mcp.CallAsync(
            "refund_payment",
            new { payment_id = first.Id.ToString("D"), amount = "80.00", currency = "EUR", confirmation_token = token });

        Assert.False(replayFailed);
        Assert.True(replay.GetProperty("replayed").GetBoolean());
        Assert.Equal("refunded", replay.GetProperty("result").GetString());

        // The proof: one Refund row in the whole database, on the payment the human approved.
        Assert.Equal(1, await RefundRowsAsync(first.Id));
        Assert.Equal(0, await RefundRowsAsync(second.Id));
        Assert.Equal(1, await QuerySqlAsync<long>(
            "select count(*) from payment_operation where kind = 'Refund'"));
        Assert.Equal(80.00m, await QuerySqlAsync<decimal>(
            "select refunded_amount from payment where id = @Id", new { first.Id }));
    }

    [Fact]
    public async Task A_read_only_agent_cannot_refund()
    {
        // How an agent loses money if this breaks: a key meant for reading moves money,
        // so the narrow scope that was supposed to contain the agent contains nothing.
        var captured = await CapturedPaymentAsync(80m);

        string readOnlyKey = await InsertKeyAsync(["payments:read"], kind: "agent");
        await using var mcp = await McpEndToEndHarness.StartAsync(Fixture, readOnlyKey);

        var (proposeFailed, proposal) = await mcp.CallAsync(
            "refund_payment", new { payment_id = captured.Id.ToString("D"), amount = "80.00", currency = "EUR" });

        // Phase 1 only reads, so payments:read is enough and the proposal is made.
        Assert.False(proposeFailed);
        Assert.Equal("confirmation_required", proposal.GetProperty("result").GetString());

        string token = proposal.GetProperty("confirmation_token").GetString()!;

        var (confirmFailed, refused) = await mcp.CallAsync(
            "refund_payment",
            new { payment_id = captured.Id.ToString("D"), amount = "80.00", currency = "EUR", confirmation_token = token });

        // The API refuses at the moment of the refund. The MCP server never checks a scope.
        Assert.True(confirmFailed);
        Assert.Equal("insufficient_scope", refused.GetProperty("error").GetString());

        Assert.Equal(0, await RefundRowsAsync(captured.Id));
        Assert.Equal(0m, await QuerySqlAsync<decimal>(
            "select refunded_amount from payment where id = @Id", new { captured.Id }));
    }

    [Fact]
    public async Task An_agent_sees_only_its_own_merchant()
    {
        // How an agent loses money if this breaks: the agent reads, and can then refund,
        // a payment belonging to a merchant it was never given.
        var m1Payment = await CapturedPaymentAsync(80m);

        string otherKey = await InsertKeyAsync(["payments:read"], kind: "agent", merchant: OtherMerchant);
        await using var mcp = await McpEndToEndHarness.StartAsync(Fixture, otherKey);

        var (getFailed, got) = await mcp.CallAsync(
            "get_payment", new { payment_id = m1Payment.Id.ToString("D") });

        // The same answer as an id that does not exist. "Not yours" would still be a leak.
        Assert.False(getFailed);
        Assert.Equal("not_found", got.GetProperty("result").GetString());

        var (listFailed, list) = await mcp.CallAsync("list_payments");

        Assert.False(listFailed);
        Assert.Empty(list.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task A_dropped_answer_still_refunds_once()
    {
        // How an agent loses money if this breaks: the refund runs, the answer is lost on the way
        // back, the agent retries exactly as it was told to — and the customer is paid twice.
        // This is the one test that needs the API's replay. Without it there are two Refund rows.
        var captured = await CapturedPaymentAsync(80m);

        string agentKey = await InsertKeyAsync(["payments:read", "payments:refund"], kind: "agent");

        using var brokenNetwork = new DropFirstAnswer();
        await using var mcp = await McpEndToEndHarness.StartAsync(Fixture, agentKey, brokenNetwork);

        var arguments = new { payment_id = captured.Id.ToString("D"), amount = "80.00", currency = "EUR" };

        var (proposeFailed, proposal) = await mcp.CallAsync("refund_payment", arguments);

        Assert.False(proposeFailed);

        string token = proposal.GetProperty("confirmation_token").GetString()!;

        var confirm = new
        {
            arguments.payment_id,
            arguments.amount,
            arguments.currency,
            confirmation_token = token
        };

        // The refund reaches the API and runs. Then the answer is thrown away.
        var (lostFailed, lost) = await mcp.CallAsync("refund_payment", confirm);

        Assert.True(lostFailed);
        Assert.Equal("refund_in_doubt", lost.GetProperty("error").GetString());

        // The money moved, even though nobody was told. This is why guessing is not allowed.
        Assert.Equal(1, await RefundRowsAsync(captured.Id));

        // The retry the tool asked for, with the same token, so the same Idempotency-Key.
        var (retryFailed, retried) = await mcp.CallAsync("refund_payment", confirm);

        Assert.False(retryFailed);
        Assert.Equal("refunded", retried.GetProperty("result").GetString());

        // The proof: one Refund row, and 80.00 refunded once. The API replayed the first answer.
        Assert.Equal(1, await RefundRowsAsync(captured.Id));
        Assert.Equal(1, await QuerySqlAsync<long>(
            "select count(*) from payment_operation where kind = 'Refund'"));
        Assert.Equal(80.00m, await QuerySqlAsync<decimal>(
            "select refunded_amount from payment where id = @Id", new { captured.Id }));
    }

    /// <summary>Counts the Refund rows on one payment, straight from Postgres.</summary>
    private Task<long> RefundRowsAsync(Guid paymentId) => QuerySqlAsync<long>(
        "select count(*) from payment_operation where payment_id = @Id and kind = 'Refund'",
        new { Id = paymentId });
}

/// <summary>
/// Loses the answer to the first refund, after the API has already done it. The request goes
/// through and only the reply is thrown away, because that is the real failure: a timeout tells
/// the caller nothing about whether the work happened. Every later call passes through.
/// </summary>
internal sealed class DropFirstAnswer : DelegatingHandler
{
    private int _refunds;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Await first. The work must really happen, or the test proves nothing.
        var response = await base.SendAsync(request, cancellationToken);

        bool isRefund = request.Method == HttpMethod.Post
            && request.RequestUri?.AbsolutePath.EndsWith("/refund", StringComparison.Ordinal) == true;

        if (isRefund && Interlocked.Increment(ref _refunds) == 1)
        {
            response.Dispose();

            throw new HttpRequestException("The answer was lost on the way back.");
        }

        return response;
    }
}
