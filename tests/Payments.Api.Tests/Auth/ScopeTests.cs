using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Xunit;

namespace Payments.Api.Tests;

/// <summary>Scopes: "may you do this?". Red until step 4.</summary>
public sealed class ScopeTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task A_read_key_can_get_and_list()
    {
        var payment = await CreatePaymentAsync();
        using var reader = await ClientWith(["payments:read"]);

        var get = await reader.GetAsync($"/payments/{payment.Id}", TestContext.Current.CancellationToken);
        var list = await reader.GetAsync("/payments", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task A_read_key_cannot_refund()
    {
        var captured = await CapturedPaymentAsync(100m);
        using var reader = await ClientWith(["payments:read"]);

        var response = await PostAsync($"/payments/{captured.Id}/refund", new AmountRequest(50m, "EUR"), client: reader);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("insufficient_scope", await ErrorCodeAsync(response));
        Assert.Equal("payments:refund", await RequiredScopeAsync(response));
    }

    [Fact]
    public async Task A_forbidden_refund_writes_nothing()
    {
        var captured = await CapturedPaymentAsync(100m);
        using var reader = await ClientWith(["payments:read"]);

        var response = await PostAsync($"/payments/{captured.Id}/refund", new AmountRequest(50m, "EUR"), client: reader);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await QuerySqlAsync<long>(
            "select count(*) from payment_operation where payment_id = @Id and kind = 'Refund'", new { captured.Id }));
        Assert.Equal(0m, await QuerySqlAsync<decimal>(
            "select refunded_amount from payment where id = @Id", new { captured.Id }));
    }

    [Fact]
    public async Task A_refund_key_cannot_create_a_payment()
    {
        using var refunder = await ClientWith(["payments:read", "payments:refund"]);

        var response = await PostAsync("/payments", new CreatePaymentRequest(SeededMerchant, 100m, "EUR"), client: refunder);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("insufficient_scope", await ErrorCodeAsync(response));
        Assert.Equal("payments:write", await RequiredScopeAsync(response));
    }

    [Fact]
    public async Task An_agent_key_with_approve_is_refused_by_the_database()
    {
        string[] scopes = ["payments:read", "payments:approve"];

        var refused = await Assert.ThrowsAsync<PostgresException>(() => InsertKeyAsync(scopes, kind: "agent"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refused.SqlState);   // 23514

        await InsertKeyAsync(scopes, kind: "service");
    }

    /// <summary>Reads the "required_scope" extension out of a ProblemDetails body.</summary>
    private static async Task<string?> RequiredScopeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var problem = JsonSerializer.Deserialize<ProblemDetails>(body, JsonSerializerOptions.Web);
        return problem!.Extensions.TryGetValue("required_scope", out var scope) && scope is JsonElement json
            ? json.GetString()
            : null;
    }
}
