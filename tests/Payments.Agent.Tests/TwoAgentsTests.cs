using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Payments.Agent.Storage;
using Payments.Agent.Tools;
using Xunit;

namespace Payments.Agent.Tests;

/// <summary>
/// xUnit only finds a collection definition in its own assembly, so this project keeps its
/// own, next to the one in <c>Payments.Api.Tests</c>. One Postgres container for the whole run.
/// </summary>
[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;

/// <summary>
/// Two agents, one payment, one refund. This is the end-to-end proof of the row lock from the
/// side that matters: nothing here is a stub below the loop. Each agent has its <b>own</b> API
/// key, its own <c>Payments.Mcp</c> server, its own run store and its own human, and both run
/// against the real API on the real throwaway Postgres.
/// <para>
/// Both proposals succeed, and they are supposed to. At proposal time 100.00 is refundable for
/// both agents, so both are told "yes, you may ask" — a proposal is a promise to <b>ask</b>, it
/// is never a reservation. The race is decided later, at the confirm, inside the API, by the
/// <c>payment</c> row's own update count.
/// </para>
/// <para>
/// The second test is the one that guards a customer. The loser must say the refund did
/// <b>not</b> happen. An agent that reports "refunded" because it meant to refund is the most
/// dangerous bug in this lab, and the sentence is written by the host from the API's answer,
/// never by the model.
/// </para>
/// </summary>
[Collection("api")]
public sealed class TwoAgentsTests(ApiFixture fixture) : IAsyncLifetime
{
    /// <summary>The first seeded merchant. Respawn keeps the merchant rows.</summary>
    private static readonly Guid SeededMerchant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public ValueTask InitializeAsync() => new(fixture.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Two_agents_refunding_one_payment_refund_once()
    {
        // How a customer loses money if this breaks: both agents are approved for 60.00 on a
        // captured 100.00, both confirms land, and 120.00 goes back on a 100.00 payment.
        var (payment, runs) = await RaceAsync();

        Assert.All(runs, run => Assert.Equal(RunStatus.Done, run.Status));

        Assert.Single(runs, run => run.Answer!.StartsWith("Refunded 60.00 EUR", StringComparison.Ordinal));
        Assert.Single(runs, run => run.Answer!.StartsWith("No money moved.", StringComparison.Ordinal));

        // The two claims no agent's own answer can make: the column and the ledger.
        Assert.Equal(60.00m, await ScalarAsync<decimal>(
            "select refunded_amount from payment where id = @id", payment));

        Assert.Equal(1L, await ScalarAsync<long>(
            "select count(*) from payment_operation where payment_id = @id and kind = 'Refund'",
            payment));
    }

    [Fact]
    public async Task The_losing_agent_reports_amount_exceeds_captured()
    {
        // How a customer loses money if this breaks: the loser tells a support person the
        // refund is done, the person tells the customer, and nobody refunds anything.
        var (_, runs) = await RaceAsync();

        var loser = Assert.Single(
            runs, run => run.Answer!.StartsWith("No money moved.", StringComparison.Ordinal));

        Assert.Equal(RunStatus.Done, loser.Status);

        // The host's own words, from the API's answer. "Refunded" must not be anywhere in them.
        Assert.DoesNotContain("Refunded", loser.Answer);
        Assert.Contains("would not do it", loser.Answer);

        // Each racer's store holds exactly one run, so Only() finds the loser's own store.
        var saved = Racers
            .Select(racer => racer.Store.Only()!)
            .Single(run => run.Run.Id == loser.Id);

        var confirm = saved.Steps.Last(step => step.Kind == "tool" && step.ToolName == RefundTool);

        Assert.Equal("ToolError", confirm.Outcome);
        Assert.Equal("lk", confirm.Approver);

        using var body = JsonDocument.Parse(confirm.OutputJson!);

        Assert.Equal("amount_exceeds_captured", body.RootElement.GetProperty("error").GetString());
    }

    /// <summary>The tool name. It is <c>internal</c> in the agent, so the test spells it out.</summary>
    private const string RefundTool = "refund_payment";

    /// <summary>The racers of the last <see cref="RaceAsync"/>, so a test can read their stores.</summary>
    private List<Racer> Racers { get; } = [];

    /// <summary>
    /// One captured 100.00 payment, two agents, and both confirms fired together.
    /// <para>
    /// The gate is the whole trick. Each agent's human says yes only once <b>both</b> agents
    /// have been asked, so neither confirm can be sent, land and finish while the other agent
    /// is still talking to the model. Without it the second agent would read a payment that is
    /// already part-refunded, and the test would pass while proving nothing.
    /// </para>
    /// </summary>
    private async Task<(Guid Payment, AgentRun[] Runs)> RaceAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var payment = await CapturedPaymentAsync(100m, ct);

        var gate = new RaceGate(parties: 2);

        var first = await StartRacerAsync("support-bot", payment, gate, ct);
        var second = await StartRacerAsync("second-bot", payment, gate, ct);

        try
        {
            string request = $"Refund 60.00 EUR on payment {payment:D}.";

            var runs = await Task.WhenAll(
                first.Loop.StartAsync(request, ct),
                second.Loop.StartAsync(request, ct));

            // Both humans were asked, and both were asked before either confirm left.
            Assert.Equal(1, first.Approvals.Asks);
            Assert.Equal(1, second.Approvals.Asks);

            return (payment, runs);
        }
        finally
        {
            await first.DisposeAsync();
            await second.DisposeAsync();
        }
    }

    /// <summary>
    /// One agent, wired the way the console wires it: its own key, its own MCP server on the
    /// real API's handler, the real <see cref="McpToolHost"/>. Only the model and the human are
    /// fakes, because a live model would make the race a coin toss about timing.
    /// </summary>
    private async Task<Racer> StartRacerAsync(string name, Guid payment, RaceGate gate, CancellationToken ct)
    {
        string key = await fixture.InsertKeyAsync(
            ["payments:read", "payments:refund"], kind: "agent", merchant: SeededMerchant);

        var mcp = await InMemoryMcpServer.StartAsync(
            new Dictionary<string, string?>
            {
                ["PAYMENTS_API_URL"] = fixture.Factory.Server.BaseAddress.ToString(),
                ["PAYMENTS_API_KEY"] = key,
            },

            // The line that makes this end to end: the MCP server's socket is the test server.
            api => api.ConfigurePrimaryHttpMessageHandler(() => fixture.Factory.Server.CreateHandler()));

        var tools = await McpToolHost.CreateAsync(mcp.Client, ct);

        // One reply, and one only. The turn ends at the proposal, and the confirm is the host's
        // own call, so a second reply would mean the loop asked the model about money.
        var model = new ScriptedChatClient().Calls(
            RefundTool,
            new Dictionary<string, object?>
            {
                ["payment_id"] = payment.ToString("D"),
                ["amount"] = "60.00",
                ["currency"] = "EUR",
            });

        var store = new InMemoryAgentRunStore();
        var approvals = new GateApprovalChannel(gate);

        var racer = new Racer(
            mcp,
            store,
            approvals,
            new AgentLoop(
                model,
                tools,
                approvals,
                store,
                new AgentOptions { AgentName = name },
                new FakeTimeProvider()));

        Racers.Add(racer);

        return racer;
    }

    /// <summary>Creates, authorises and captures one payment with a service key.</summary>
    private async Task<Guid> CapturedPaymentAsync(decimal amount, CancellationToken ct)
    {
        using var client = fixture.Factory.CreateClient();

        client.DefaultRequestHeaders.Add("X-Api-Key", await fixture.InsertKeyAsync(
            ["payments:read", "payments:write", "payments:refund"],
            kind: "service",
            merchant: SeededMerchant));

        var created = await PostAsync(
            client, "/payments", new { merchantId = SeededMerchant, amount, currency = "EUR" }, ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var payment = (await created.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("id").GetGuid();

        Assert.Equal(
            HttpStatusCode.OK,
            (await PostAsync(client, $"/payments/{payment:D}/authorise", null, ct)).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await PostAsync(
                client,
                $"/payments/{payment:D}/capture",
                new { amount, currency = "EUR" },
                ct)).StatusCode);

        return payment;
    }

    /// <summary>A POST with a fresh Idempotency-Key. Every money-moving POST needs one.</summary>
    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        return await client.SendAsync(request, ct);
    }

    /// <summary>Reads one value straight from Postgres, past the API and past both agents.</summary>
    private async Task<T> ScalarAsync<T>(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);

        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    /// <summary>One agent and everything it owns alone.</summary>
    private sealed record Racer(
        InMemoryMcpServer Mcp,
        InMemoryAgentRunStore Store,
        GateApprovalChannel Approvals,
        AgentLoop Loop) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Mcp.DisposeAsync();
    }

    /// <summary>
    /// Opens once every racer has arrived, and then lets them all through at once. A
    /// <c>Barrier</c> would block the thread, and these waiters are in an async call chain.
    /// </summary>
    private sealed class RaceGate(int parties)
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _arrived;

        public Task ArriveAndWaitAsync(CancellationToken ct)
        {
            if (Interlocked.Increment(ref _arrived) == parties)
            {
                _open.TrySetResult();
            }

            // WaitAsync, so a gate that never opens fails the test instead of hanging it.
            return _open.Task.WaitAsync(ct);
        }
    }

    /// <summary>
    /// The human, holding at the gate. It says yes, but only once the other agent's human has
    /// been asked too, so the two confirms leave together.
    /// </summary>
    private sealed class GateApprovalChannel(RaceGate gate) : IApprovalChannel
    {
        public int Asks { get; private set; }

        public async Task<Approval> AskAsync(AgentRun run, PendingRefund refund, CancellationToken ct)
        {
            Asks++;

            await gate.ArriveAndWaitAsync(ct);

            return new Approval(Approved: true, By: "lk");
        }
    }
}
