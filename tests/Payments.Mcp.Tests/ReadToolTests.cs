using System.Net;
using System.Text.Json;
using Xunit;

namespace Payments.Mcp.Tests;

/// <summary>
/// The two read tools. The rule they share: a miss is a fact, not a failure.
/// An id that does not exist is result: not_found, which the model reads and moves on from.
/// A 403 is a real error, with a hint that only a human can clear it.
/// </summary>
public sealed class ReadToolTests
{
    private const string GoodPaymentId = "0199c3f0-1a2b-7c3d-8e4f-5a6b7c8d9e0f";

    /// <summary>One payment body in the shape the API returns.</summary>
    private static object PaymentBody(string id = GoodPaymentId) => new
    {
        id,
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

    /// <summary>A ProblemDetails body in the shape the API returns, with its "code".</summary>
    private static object Problem(int status, string code, string detail) => new
    {
        type = $"urn:payments-labs:error:{code}",
        title = code,
        status,
        detail,
        instance = "/payments",
        code,
    };

    [Fact]
    public async Task Get_payment_returns_the_payment()
    {
        await using var harness = await McpHarness.StartAsync();

        harness.Api.Enqueue(HttpStatusCode.OK, PaymentBody());

        var (isError, body) = await harness.CallAsync("get_payment", new { payment_id = GoodPaymentId });

        Assert.False(isError);
        Assert.Equal("found", body.GetProperty("result").GetString());
        Assert.Equal(GoodPaymentId, body.GetProperty("payment").GetProperty("id").GetString());
    }

    [Fact]
    public async Task A_missing_payment_is_a_result_not_an_error()
    {
        await using var harness = await McpHarness.StartAsync();

        harness.Api.Enqueue(
            HttpStatusCode.NotFound,
            Problem(404, "payment_not_found", "There is no payment with that id."));

        var (isError, body) = await harness.CallAsync("get_payment", new { payment_id = GoodPaymentId });

        // Not an error. An agent that reads "not_found" as a failure retries, and a
        // retry loop on a miss is how an agent burns a conversation.
        Assert.False(isError);
        Assert.Equal("not_found", body.GetProperty("result").GetString());
        Assert.Equal(GoodPaymentId, body.GetProperty("payment_id").GetString());
    }

    [Fact]
    public async Task A_403_is_insufficient_scope_with_a_hint()
    {
        await using var harness = await McpHarness.StartAsync();

        harness.Api.Enqueue(
            HttpStatusCode.Forbidden,
            Problem(403, "insufficient_scope", "This key does not have payments:read."));

        var (isError, body) = await harness.CallAsync("get_payment", new { payment_id = GoodPaymentId });

        // The model cannot fix a missing scope by trying again. The hint must send it to a person.
        Assert.True(isError);
        Assert.Equal("insufficient_scope", body.GetProperty("error").GetString());

        var hint = body.GetProperty("hint").GetString();
        Assert.False(string.IsNullOrWhiteSpace(hint));
        Assert.Contains("human", hint!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// CF-031. A 401 used to fall through to the tool_failed arm, whose hint is
    /// "try once more" -- the one thing that cannot help an unknown key. The model
    /// then retried until the step cap stopped it.
    /// </summary>
    [Fact]
    public async Task A_401_is_api_key_invalid_with_a_stop_hint()
    {
        await using var harness = await McpHarness.StartAsync();

        harness.Api.Enqueue(
            HttpStatusCode.Unauthorized,
            Problem(401, "api_key_invalid", "The API key is not valid."));

        var (isError, body) = await harness.CallAsync("get_payment", new { payment_id = GoodPaymentId });

        Assert.True(isError);
        Assert.Equal("api_key_invalid", body.GetProperty("error").GetString());

        // The hint must stop the model, not send it round again.
        var hint = body.GetProperty("hint").GetString();
        Assert.False(string.IsNullOrWhiteSpace(hint));
        Assert.Contains("human", hint!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("try", hint!, StringComparison.OrdinalIgnoreCase);

        // The API's own words never reach the model: the key itself may be echoed in them.
        Assert.DoesNotContain("The API key is not valid", body.ToString());

        // One call, not two. The model was never invited back.
        Assert.Single(harness.Api.Requests);
    }

    [Fact]
    public async Task List_passes_status_and_limit_through()
    {
        await using var harness = await McpHarness.StartAsync();

        harness.Api.Enqueue(HttpStatusCode.OK, new { items = Array.Empty<object>(), truncated = false });

        await harness.CallAsync("list_payments", new { status = "Captured", limit = 5 });

        var request = Assert.Single(harness.Api.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.StartsWith("/payments", request.PathAndQuery, StringComparison.Ordinal);
        Assert.Contains("status=Captured", request.PathAndQuery, StringComparison.Ordinal);
        Assert.Contains("limit=5", request.PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_defaults_to_20()
    {
        await using var harness = await McpHarness.StartAsync();

        harness.Api.Enqueue(HttpStatusCode.OK, new { items = Array.Empty<object>(), truncated = false });

        await harness.CallAsync("list_payments");

        var request = Assert.Single(harness.Api.Requests);
        Assert.Contains("limit=20", request.PathAndQuery, StringComparison.Ordinal);
        Assert.DoesNotContain("status=", request.PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_passes_truncated_through_with_a_hint()
    {
        await using var harness = await McpHarness.StartAsync();

        harness.Api.Enqueue(HttpStatusCode.OK, new
        {
            items = new[]
            {
                new { id = GoodPaymentId, status = "Captured", currency = "EUR",
                      authorisedAmount = 80.00m, capturedAmount = 80.00m, refundedAmount = 0.00m,
                      createdAt = "2026-10-21T10:00:00+00:00" },
            },
            truncated = true,
        });

        var (isError, body) = await harness.CallAsync("list_payments");

        Assert.False(isError);
        Assert.True(body.GetProperty("truncated").GetBoolean());

        // There is no paging. The only way to see less is to narrow, so the hint must say so.
        var hint = body.GetProperty("hint").GetString();
        Assert.Contains("status", hint ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
