using System.Globalization;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Payments.Mcp.Tests;

/// <summary>
/// Phase 2 of the refund: the confirm. One approved token buys at most one refund.
/// Every test here is a way an agent pays a customer twice.
/// </summary>
public sealed class RefundConfirmTests
{
    private const string GoodPaymentId = "0199c3f0-1a2b-7c3d-8e4f-5a6b7c8d9e0f";

    /// <summary>A captured payment with 80.00 EUR left to refund.</summary>
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

    /// <summary>The same payment after the refund went through.</summary>
    private static object RefundedBody() => new
    {
        id = GoodPaymentId,
        merchantId = "0199c000-0000-7000-8000-000000000001",
        status = "Refunded",
        currency = "EUR",
        authorisedAmount = 80.00m,
        capturedAmount = 80.00m,
        refundedAmount = 80.00m,
        capturableRemaining = 0.00m,
        refundableRemaining = 0.00m,
        allowedActions = Array.Empty<string>(),
        declineReason = (string?)null,
        createdAt = "2026-10-21T10:00:00+00:00",
        updatedAt = "2026-10-21T10:05:00+00:00",
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

    /// <summary>
    /// Runs phase 1 and returns the token. It asserts the proposal worked, so a
    /// broken phase 1 fails here with the tool's own words instead of a null token.
    /// </summary>
    private static async Task<string> ProposeAsync(McpHarness harness)
    {
        harness.Api.Enqueue(HttpStatusCode.OK, PaymentBody());

        var (isError, body) = await harness.CallAsync("refund_payment", new
        {
            payment_id = GoodPaymentId,
            amount = "80.00",
            currency = "EUR",
        });

        Assert.False(isError, body.ToString());

        return body.GetProperty("confirmation_token").GetString()!;
    }

    /// <summary>Phase 2: the same arguments plus the token.</summary>
    private static Task<(bool IsError, JsonElement Body)> ConfirmAsync(
        McpHarness harness, string token, string amount = "80.00")
        => harness.CallAsync("refund_payment", new
        {
            payment_id = GoodPaymentId,
            amount,
            currency = "EUR",
            confirmation_token = token,
        });

    private static IReadOnlyList<RecordedRequest> Posts(McpHarness harness)
        => harness.Api.Requests.Where(r => r.Method == HttpMethod.Post).ToList();

    /// <summary>The amount in a POST body. A string and a number read the same here.</summary>
    private static decimal AmountOf(RecordedRequest post)
    {
        using var parsed = JsonDocument.Parse(post.Body);
        var amount = parsed.RootElement.GetProperty("amount");

        return amount.ValueKind is JsonValueKind.String
            ? decimal.Parse(amount.GetString()!, CultureInfo.InvariantCulture)
            : amount.GetDecimal();
    }

    private static string CurrencyOf(RecordedRequest post)
    {
        using var parsed = JsonDocument.Parse(post.Body);

        return parsed.RootElement.GetProperty("currency").GetString()!;
    }

    [Fact]
    public async Task A_confirm_posts_exactly_one_refund()
    {
        await using var harness = await McpHarness.StartAsync();

        var token = await ProposeAsync(harness);

        harness.Api.Enqueue(HttpStatusCode.OK, RefundedBody());

        var (isError, body) = await ConfirmAsync(harness, token);

        Assert.False(isError, body.ToString());
        Assert.Equal("refunded", body.GetProperty("result").GetString());

        // How the money is lost: two POSTs for one approval is two refunds. The
        // approved refund must leave this server exactly once, with these values.
        var post = Assert.Single(Posts(harness));
        Assert.Equal($"/payments/{GoodPaymentId}/refund", post.PathAndQuery);
        Assert.Equal(80.00m, AmountOf(post));
        Assert.Equal("EUR", CurrencyOf(post));
    }

    [Fact]
    public async Task The_refund_carries_an_idempotency_key_from_the_token()
    {
        await using var harness = await McpHarness.StartAsync();

        var token = await ProposeAsync(harness);

        harness.Api.Enqueue(HttpStatusCode.OK, RefundedBody());

        await ConfirmAsync(harness, token);

        // How the money is lost: with no key, or a fresh key per try, the API cannot
        // tell a retry from a second refund. One approval is one key, for ever.
        var post = Assert.Single(Posts(harness));
        Assert.Equal("mcp-" + token, post.Headers["Idempotency-Key"]);
    }

    [Fact]
    public async Task A_repeated_confirm_replays_and_posts_nothing()
    {
        await using var harness = await McpHarness.StartAsync();

        var token = await ProposeAsync(harness);

        harness.Api.Enqueue(HttpStatusCode.OK, RefundedBody());

        var (_, first) = await ConfirmAsync(harness, token);
        var (_, second) = await ConfirmAsync(harness, token);

        // How the money is lost: the model loses its place in the conversation and
        // confirms again. The second confirm must tell the same story, not refund again.
        Assert.Single(Posts(harness));
        Assert.Equal(
            first.GetProperty("result").GetString(),
            second.GetProperty("result").GetString());
        Assert.True(second.GetProperty("replayed").GetBoolean());
    }

    [Fact]
    public async Task A_changed_amount_is_token_mismatch()
    {
        await using var harness = await McpHarness.StartAsync();

        var token = await ProposeAsync(harness);

        var (isError, body) = await ConfirmAsync(harness, token, amount: "79.99");

        // How the money is lost: the human approved 80.00 and the model sends 79.99,
        // or 800.00. A token must buy the refund that was read out, and nothing else.
        Assert.True(isError, body.ToString());
        Assert.Equal("token_mismatch", body.GetProperty("error").GetString());
        Assert.Empty(Posts(harness));
    }

    [Fact]
    public async Task An_expired_token_posts_nothing()
    {
        await using var harness = await McpHarness.StartAsync();

        var token = await ProposeAsync(harness);

        harness.Clock.Advance(TimeSpan.FromMinutes(5));

        var (isError, body) = await ConfirmAsync(harness, token);

        // How the money is lost: a yes from an hour ago is not a yes now. The payment
        // may have been refunded, voided or captured again while nobody was watching.
        Assert.True(isError, body.ToString());
        Assert.Equal("token_expired", body.GetProperty("error").GetString());
        Assert.Empty(Posts(harness));
    }

    [Fact]
    public async Task An_invented_token_is_token_unknown()
    {
        await using var harness = await McpHarness.StartAsync();

        var (isError, body) = await ConfirmAsync(harness, "rf_madeup");

        // How the money is lost: a model that can write its own token has no gate at
        // all. Only a token this server issued may move money.
        Assert.True(isError, body.ToString());
        Assert.Equal("token_unknown", body.GetProperty("error").GetString());
        Assert.Empty(Posts(harness));
    }

    [Fact]
    public async Task A_403_on_confirm_is_insufficient_scope()
    {
        await using var harness = await McpHarness.StartAsync();

        var token = await ProposeAsync(harness);

        harness.Api.Enqueue(
            HttpStatusCode.Forbidden,
            Problem(403, "insufficient_scope", "This key does not have payments:refund."));

        var (isError, body) = await ConfirmAsync(harness, token);

        Assert.True(isError, body.ToString());
        Assert.Equal("insufficient_scope", body.GetProperty("error").GetString());

        // How the money is lost: a refused refund is a final answer. An agent that
        // retries it hammers the API, and one widened scope later it pays twice.
        var (_, again) = await ConfirmAsync(harness, token);
        Assert.Equal("insufficient_scope", again.GetProperty("error").GetString());
        Assert.True(again.GetProperty("replayed").GetBoolean());
        Assert.Single(Posts(harness));
    }

    [Fact]
    public async Task A_lost_answer_is_in_doubt_and_the_retry_sends_the_same_key()
    {
        await using var harness = await McpHarness.StartAsync();

        var token = await ProposeAsync(harness);

        // The first POST never comes back; the second one works.
        harness.Api.EnqueueThrow(new HttpRequestException("the connection was closed"));
        harness.Api.Enqueue(HttpStatusCode.OK, RefundedBody());

        var (firstIsError, first) = await ConfirmAsync(harness, token);

        // How the money is lost: nobody knows whether that refund happened. A tool
        // that guesses "it failed" refunds twice; one that guesses "it worked" leaves
        // the customer unpaid. So it must say it does not know.
        Assert.True(firstIsError, first.ToString());
        Assert.Equal("refund_in_doubt", first.GetProperty("error").GetString());

        var (secondIsError, second) = await ConfirmAsync(harness, token);

        Assert.False(secondIsError, second.ToString());
        Assert.Equal("refunded", second.GetProperty("result").GetString());

        // The same key on both tries is what lets the API answer the retry from its
        // record instead of refunding again.
        var posts = Posts(harness);
        Assert.Equal(2, posts.Count);
        Assert.Equal("mcp-" + token, posts[0].Headers["Idempotency-Key"]);
        Assert.Equal(posts[0].Headers["Idempotency-Key"], posts[1].Headers["Idempotency-Key"]);
    }

    [Fact]
    public async Task Two_confirms_at_once_post_once()
    {
        await using var harness = await McpHarness.StartAsync();

        var token = await ProposeAsync(harness);

        // The stub holds the first refund open, so both confirms are in flight at once.
        var held = harness.Api.EnqueueGated(HttpStatusCode.OK, RefundedBody());

        var firstConfirm = ConfirmAsync(harness, token);

        // No sleep: either the POST reaches the stub, or the first confirm already
        // answered, which means the tool never posted and the asserts below say so.
        await Task.WhenAny(held.Arrived, firstConfirm);

        var (secondIsError, second) = await ConfirmAsync(harness, token);

        // How the money is lost: two clicks, or two agents on one approval. The second
        // one must be told to wait, not given its own refund.
        Assert.True(secondIsError, second.ToString());
        Assert.Equal("confirmation_in_progress", second.GetProperty("error").GetString());

        held.Release();

        var (firstIsError, first) = await firstConfirm;

        Assert.False(firstIsError, first.ToString());
        Assert.Equal("refunded", first.GetProperty("result").GetString());
        Assert.Single(Posts(harness));
    }
}
