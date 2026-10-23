using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;
using Payments.Agent.Storage;
using Payments.Agent.Tools;
using Xunit;

namespace Payments.Agent.Tests;

/// <summary>
/// The run survives the process. These six are the only tests in the lab that use the real
/// <see cref="EfAgentRunStore"/> on a real SQL Server, because the in-memory store cannot lose
/// what a process loses: a dictionary that outlives the "crash" would let a broken resume pass.
/// <para>
/// A <b>crash</b> here is the fake model throwing, or the store throwing, in the middle of a
/// run: <c>StartAsync</c> leaves the method by exception, exactly as a killed process leaves it.
/// A <b>restart</b> is a new <see cref="Life"/>: a new loop, a new fake model, a new
/// <c>InMemoryMcpServer</c> (so every confirmation token from before is gone with the server
/// that minted it) and a new store on the same database. Nothing is shared but the database and
/// the <see cref="StubApi"/>, because in real life the API outlives the agent that called it.
/// </para>
/// <para>
/// Two of the six guard money. A wait resumed after a restart must ask the human <b>again</b>,
/// because the old token died with the old server and a yes given to a vanished proposal is
/// not a yes. An approval with no confirm after it must <b>never</b> ask again: the first
/// confirm may already have moved the money, and nobody can tell from here.
/// </para>
/// </summary>
[Trait("runs", "local")]
public sealed class ResumeTests(SqlServerDatabase db) : IClassFixture<SqlServerDatabase>
{
    /// <summary>Four payments, so a run needs four reads and a crash can land between them.</summary>
    private static readonly string[] Ids =
    [
        "0199c3f0-1a2b-7c3d-8e4f-5a6b7c8d9e01",
        "0199c3f0-1a2b-7c3d-8e4f-5a6b7c8d9e02",
        "0199c3f0-1a2b-7c3d-8e4f-5a6b7c8d9e03",
        "0199c3f0-1a2b-7c3d-8e4f-5a6b7c8d9e04",
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_crashed_run_resumes_at_the_next_step()
    {
        var api = new StubApi();

        var runId = await CrashAfterThreeReadsAsync(api);

        await using var second = await Life.StartAsync(db, api);

        api.Enqueue(HttpStatusCode.OK, StubApi.Payment(Ids[3]));

        second.Model
            .Calls("get_payment", Read(Ids[3]))
            .Says("All four payments are captured.");

        var run = await second.Loop.ResumeAsync(runId, Ct);

        Assert.Equal(RunStatus.Done, run.Status);

        var steps = (await second.Store.LoadAsync(runId, Ct))!.Steps;

        // 1, 2, 3 from the first life and 4 from the second. A gap means the resume lost a
        // step; a repeated ordinal means it wrote over one. Either way the history a reviewer
        // reads months later is not what happened.
        Assert.Equal<int[]>([1, 2, 3, 4], [.. steps.Select(s => s.Ordinal)]);
    }

    [Fact]
    public async Task Tools_done_before_the_crash_are_not_called_again()
    {
        var api = new StubApi();

        var runId = await CrashAfterThreeReadsAsync(api);

        await using var second = await Life.StartAsync(db, api);

        api.Enqueue(HttpStatusCode.OK, StubApi.Payment(Ids[3]));

        second.Model
            .Calls("get_payment", Read(Ids[3]))
            .Says("All four payments are captured.");

        await second.Loop.ResumeAsync(runId, Ct);

        // Four reads over two lives, one per payment. The saved messages already hold the
        // first three results, so the resumed model is told what they said instead of asking
        // again. On a read a repeat is only waste; the same bug on a refund is money moved
        // twice (lab 16).
        Assert.Equal(4, api.Requests.Count);

        foreach (var payment in Ids)
        {
            Assert.Single(api.Requests, r => r.PathAndQuery == $"/payments/{payment}");
        }
    }

    [Fact]
    public async Task A_finished_run_is_not_resumed()
    {
        var api = new StubApi();

        await using var life = await Life.StartAsync(db, api);

        var run = Seed(RunStatus.Done, "Payment 01 is captured, 80.00 EUR.");

        await life.Store.SaveAsync(run, [], Ct);

        var resumed = await life.Loop.ResumeAsync(run.Id, Ct);

        // A finished run is the answer, not a job. Carrying it on would call tools again for a
        // question somebody already got an answer to, and every retry in the platform above
        // would turn one request into many.
        Assert.Equal(RunStatus.Done, resumed.Status);
        Assert.Equal(run.Answer, resumed.Answer);
        Assert.Empty(life.Model.Requests);
        Assert.Empty(api.Requests);
    }

    [Fact]
    public async Task A_resumed_wait_proposes_again_and_asks_again()
    {
        var api = new StubApi();

        // The proposal reads the payment, in both lives. The second life's yes posts the refund.
        api.Enqueue(HttpStatusCode.OK, Captured());
        api.Enqueue(HttpStatusCode.OK, Captured());
        api.Enqueue(HttpStatusCode.OK, Refunded());

        Guid runId;
        string oldToken;

        await using (var first = await Life.StartAsync(db, api, new DyingChannel()))
        {
            first.Model.Calls("refund_payment", Refund("60.00"));

            // The process dies while the human is reading the proposal.
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => first.Loop.StartAsync("refund 60 euro on payment 01", Ct));

            runId = first.Watch.LastRunId;

            var waiting = (await first.Store.LoadAsync(runId, Ct))!.Run;

            Assert.Equal(RunStatus.WaitingForHuman, waiting.Status);

            oldToken = waiting.PendingCall!.Token;
        }

        await using var second = await Life.StartAsync(db, api);

        var run = await second.Loop.ResumeAsync(runId, Ct);

        // Asked again, on a new proposal. The old token was minted by the MCP server that just
        // died, so it confirms nothing: a resume that reused it would hand the human's old yes
        // to a gate that no longer has a lock.
        var ask = Assert.Single(second.Approvals.Asks);

        Assert.NotEqual(oldToken, ask.Refund.Token);
        Assert.Equal("60.00", ask.Refund.Amount);
        Assert.Equal(Ids[0], ask.Refund.PaymentId);

        // The second proposal is a real call to the server, and the yes is a real confirm.
        Assert.Equal(2, api.Requests.Count(r => r.Method == HttpMethod.Get));
        Assert.Single(api.Requests, r => r.Method == HttpMethod.Post);
        Assert.Equal(RunStatus.Done, run.Status);
    }

    [Fact]
    public async Task An_approval_without_a_confirm_is_in_doubt()
    {
        var api = new StubApi();

        api.Enqueue(HttpStatusCode.OK, Captured());

        Guid runId;

        // The crash lands in the one window that matters: the approval step is written, the
        // confirm has not been sent. The store throws right after it saved the approval, which
        // is where a killed process would stop.
        await using (var first = await Life.StartAsync(
            db, api, wrapStore: inner => new WatchingStore(inner)
            {
                AfterSave = steps => steps.Any(s => s.Kind == "approval")
                    ? throw new InvalidOperationException("the process died after the approval")
                    : Task.CompletedTask,
            }))
        {
            first.Model.Calls("refund_payment", Refund("60.00"));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => first.Loop.StartAsync("refund 60 euro on payment 01", Ct));

            runId = first.Watch.LastRunId;
        }

        await using var second = await Life.StartAsync(db, api);

        var run = await second.Loop.ResumeAsync(runId, Ct);

        // In doubt, and it says so. The first life may already have moved the money, and from
        // here nobody can tell. Asking the human again would be asking them to approve a
        // refund they have already approved, and a second confirm can send it twice.
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("refund_in_doubt", run.Reason);
        Assert.Empty(second.Approvals.Asks);
        Assert.DoesNotContain(api.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Two_workers_resuming_one_run_save_once()
    {
        var api = new StubApi();

        api.Enqueue(HttpStatusCode.OK, StubApi.Payment(Ids[0]));

        var gate = new PairGate();

        // Both workers load the run before either claims it. Without the gate the first one
        // claims, finishes and the second finds a Done run: the race the claim exists for
        // never happens, and the test passes with no claim at all.
        await using var first = await Life.StartAsync(
            db, api, wrapStore: inner => new WatchingStore(inner) { AfterLoad = gate.ArriveAsync });

        await using var second = await Life.StartAsync(
            db, api, wrapStore: inner => new WatchingStore(inner) { AfterLoad = gate.ArriveAsync });

        var run = Seed(RunStatus.Running, answer: null);

        await first.Store.SaveAsync(run, [], Ct);

        foreach (var life in new[] { first, second })
        {
            life.Model
                .Calls("get_payment", Read(Ids[0]))
                .Says("Payment 01 is captured, 80.00 EUR.");
        }

        var both = first.Loop.ResumeAsync(run.Id, Ct);

        await Task.WhenAll(both, second.Loop.ResumeAsync(run.Id, Ct));

        // One worker goes on; the other stops at its claim. The loser calls no model and no
        // tool: two workers carrying the same run would refund the same money twice, and the
        // database's RowVer is the only thing that can decide between them.
        var losers = new[] { first, second }.Count(l => l.Model.Requests.Count == 0);

        Assert.Equal(1, losers);
        Assert.Single(api.Requests);

        var steps = (await first.Store.LoadAsync(run.Id, Ct))!.Steps;

        Assert.Equal<int[]>([1], [.. steps.Select(s => s.Ordinal)]);
        Assert.Equal(RunStatus.Done, (await first.Store.LoadAsync(run.Id, Ct))!.Run.Status);
    }

    /// <summary>
    /// Three reads saved, then the model dies on the fourth call. Shared by the two tests that
    /// need the same crash, so they cannot drift apart.
    /// </summary>
    private async Task<Guid> CrashAfterThreeReadsAsync(StubApi api)
    {
        await using var first = await Life.StartAsync(db, api);

        for (var i = 0; i < 3; i++)
        {
            api.Enqueue(HttpStatusCode.OK, StubApi.Payment(Ids[i]));

            first.Model.Calls("get_payment", Read(Ids[i]));
        }

        first.Model.Throws();

        await Assert.ThrowsAsync<HttpRequestException>(
            () => first.Loop.StartAsync(
                "Tell me the status of all four payments, one at a time.", Ct));

        return first.Watch.LastRunId;
    }

    /// <summary>A run written straight to the store, so a test can start from a saved state.</summary>
    private static AgentRun Seed(RunStatus status, string? answer) => new()
    {
        Id = Guid.CreateVersion7(),
        AgentName = "support-bot",
        ModelId = ScriptedChatClient.FakeModelId,
        PromptVersion = "v1",
        Status = status,
        Answer = answer,
        Messages =
        [
            new ChatMessage(ChatRole.System, new AgentOptions().SystemPrompt),
            new ChatMessage(ChatRole.User, $"What is the status of payment {Ids[0]}?"),
        ],
        CreatedUtc = DateTimeOffset.UtcNow,
        UpdatedUtc = DateTimeOffset.UtcNow,
    };

    private static Dictionary<string, object?> Read(string paymentId) =>
        new() { ["payment_id"] = paymentId };

    private static Dictionary<string, object?> Refund(string amount) => new()
    {
        ["payment_id"] = Ids[0],
        ["amount"] = amount,
        ["currency"] = "EUR",
    };

    private static object Captured(decimal amount = 100.00m) => StubApi.Payment(
        Ids[0], authorised: amount, captured: amount, refundableRemaining: amount);

    private static object Refunded() => StubApi.Payment(
        Ids[0],
        status: "PartiallyRefunded",
        authorised: 100.00m,
        captured: 100.00m,
        refunded: 60.00m,
        refundableRemaining: 40.00m);

    /// <summary>
    /// One life of the agent: everything a process holds and loses when it dies. The database
    /// and the <see cref="StubApi"/> are handed in, because those are the two things that
    /// outlive it.
    /// </summary>
    private sealed class Life : IAsyncDisposable
    {
        private InMemoryMcpServer _mcp = null!;

        public ScriptedChatClient Model { get; } = new();

        public FakeApprovalChannel Approvals { get; private set; } = null!;

        /// <summary>The store the loop was given, decorator and all.</summary>
        public IAgentRunStore Store { get; private set; } = null!;

        /// <summary>The decorator, for the run id a crashed <c>StartAsync</c> never returned.</summary>
        public WatchingStore Watch { get; private set; } = null!;

        public AgentLoop Loop { get; private set; } = null!;

        public static async Task<Life> StartAsync(
            SqlServerDatabase db,
            StubApi api,
            IApprovalChannel? approvals = null,
            Func<IAgentRunStore, WatchingStore>? wrapStore = null)
        {
            var life = new Life();

            life._mcp = await InMemoryMcpServer.StartAsync(
                new Dictionary<string, string?>
                {
                    ["PAYMENTS_API_URL"] = "http://api.test/",
                    ["PAYMENTS_API_KEY"] = "test-key-123",
                },
                client => client.ConfigurePrimaryHttpMessageHandler(() => api));

            var tools = await McpToolHost.CreateAsync(life._mcp.Client);

            // One fresh DbContext per call, from a factory: two processes see the row, not one
            // change tracker. This is the whole reason these tests are on SQL Server.
            var options = new DbContextOptionsBuilder<AgentDbContext>()
                .UseSqlServer(db.ConnectionString)
                .Options;

            var real = new EfAgentRunStore(() => new AgentDbContext(options));

            life.Watch = wrapStore is null ? new WatchingStore(real) : wrapStore(real);
            life.Store = life.Watch;
            life.Approvals = new FakeApprovalChannel(life.Store);

            life.Loop = new AgentLoop(
                life.Model,
                tools,
                approvals ?? life.Approvals,
                life.Store,
                new AgentOptions { AgentName = "support-bot" },
                new FakeTimeProvider());

            return life;
        }

        public async ValueTask DisposeAsync()
        {
            if (_mcp is not null)
            {
                await _mcp.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// A store that watches, and on request dies. It is a decorator for the same reason the
    /// console's is: the loop must not learn that the process can be killed.
    /// </summary>
    private sealed class WatchingStore(IAgentRunStore inner) : IAgentRunStore
    {
        /// <summary>
        /// The id of the last run saved. A crashed <c>StartAsync</c> returns nothing, so this
        /// is how the test learns which run to resume — the same way the console prints it.
        /// </summary>
        public Guid LastRunId { get; private set; }

        /// <summary>Runs after a load has returned. The two-worker test blocks here.</summary>
        public Func<Task>? AfterLoad { get; init; }

        /// <summary>Runs after a save landed. Throw from here to kill the process.</summary>
        public Func<IReadOnlyList<AgentStep>, Task>? AfterSave { get; init; }

        public async Task<LoadedRun?> LoadAsync(Guid id, CancellationToken ct)
        {
            var loaded = await inner.LoadAsync(id, ct);

            if (AfterLoad is not null)
            {
                await AfterLoad();
            }

            return loaded;
        }

        public async Task<SaveOutcome> SaveAsync(
            AgentRun run, IReadOnlyList<AgentStep> newSteps, CancellationToken ct)
        {
            var outcome = await inner.SaveAsync(run, newSteps, ct);

            LastRunId = run.Id;

            if (AfterSave is not null)
            {
                await AfterSave(newSteps);
            }

            return outcome;
        }
    }

    /// <summary>
    /// The human was asked and the process died before they answered. This is the only honest
    /// way to leave a run saved as <c>WaitingForHuman</c>: the loop writes that state before it
    /// asks, so the crash has to happen inside the ask.
    /// </summary>
    private sealed class DyingChannel : IApprovalChannel
    {
        public Task<Approval> AskAsync(AgentRun run, PendingRefund refund, CancellationToken ct)
            => throw new InvalidOperationException("the process died while the human was reading");
    }

    /// <summary>
    /// Two callers in, both out together. A <c>Barrier</c> would hold two thread-pool threads;
    /// this holds two awaits, which is what the two resumes really are.
    /// </summary>
    private sealed class PairGate
    {
        private readonly TaskCompletionSource _both =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _arrived;

        public Task ArriveAsync()
        {
            if (Interlocked.Increment(ref _arrived) >= 2)
            {
                _both.TrySetResult();
            }

            return _both.Task;
        }
    }
}
