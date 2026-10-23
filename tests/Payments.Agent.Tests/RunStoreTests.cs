using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Payments.Agent.Storage;
using Xunit;

namespace Payments.Agent.Tests;

/// <summary>
/// The five rules <see cref="EfAgentRunStore"/> has to carry, against the real SQL Server.
/// <para>
/// Every load and every save goes through its own <see cref="AgentDbContext"/>. That is the
/// point: in the real loop the worker that resumes a run is a different process from the one
/// that started it, and a shared context would hide both the JSON round trip and the
/// <c>RowVer</c> race behind EF Core's change tracker.
/// </para>
/// <para>
/// Each test uses a fresh run id, so the tests need no reset between them.
/// </para>
/// </summary>
[Trait("runs", "local")]
public sealed class RunStoreTests(SqlServerDatabase db) : IClassFixture<SqlServerDatabase>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_run_round_trips()
    {
        var store = NewStore();
        var run = NewRun();

        Assert.Equal(SaveOutcome.Saved, await store.SaveAsync(run, [], Ct));

        var loaded = await store.LoadAsync(run.Id, Ct);

        Assert.NotNull(loaded);
        Assert.Equal(RunStatus.Running, loaded.Run.Status);
        Assert.Equal(run.StepCount, loaded.Run.StepCount);
        Assert.Equal(run.AgentName, loaded.Run.AgentName);
        Assert.Equal(run.ModelId, loaded.Run.ModelId);
        Assert.Equal(run.PromptVersion, loaded.Run.PromptVersion);
        Assert.Equal(run.CreatedUtc, loaded.Run.CreatedUtc);
    }

    [Fact]
    public async Task Messages_round_trip_with_tool_calls_and_results()
    {
        var store = NewStore();
        var run = NewRun();

        run.Messages =
        [
            new ChatMessage(ChatRole.User, "where is my refund?"),
            new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("call-1", "get_payment",
                    new Dictionary<string, object?> { ["payment_id"] = "pay_7" })
            ]),
            new ChatMessage(ChatRole.Tool,
            [
                new FunctionResultContent("call-1", """{"status":"Captured"}""")
            ])
        ];

        await store.SaveAsync(run, [], Ct);

        var loaded = await store.LoadAsync(run.Id, Ct);

        Assert.NotNull(loaded);
        Assert.Equal(3, loaded.Run.Messages.Count);

        // A tool call is a message, not a side note. If the call and its result do not survive
        // the JSON round trip, a resumed run hands the model a history it never saw and the
        // model answers from half a conversation.
        var call = Assert.IsType<FunctionCallContent>(
            Assert.Single(loaded.Run.Messages[1].Contents));

        Assert.Equal("call-1", call.CallId);
        Assert.Equal("get_payment", call.Name);
        Assert.NotNull(call.Arguments);
        Assert.Equal("pay_7", call.Arguments["payment_id"]?.ToString());

        var result = Assert.IsType<FunctionResultContent>(
            Assert.Single(loaded.Run.Messages[2].Contents));

        // The call id is the thread between the ask and the answer. Lose it and the model
        // cannot tell which tool result belongs to which call.
        Assert.Equal("call-1", result.CallId);
        Assert.Contains("Captured", result.Result?.ToString());
    }

    [Fact]
    public async Task Steps_come_back_in_ordinal_order()
    {
        var store = NewStore();
        var run = NewRun();

        // Saved out of order on purpose: the order has to come from the ORDER BY, not from
        // the order rows happen to sit in.
        AgentStep[] steps = [NewStep(run.Id, 3), NewStep(run.Id, 1), NewStep(run.Id, 2)];

        await store.SaveAsync(run, steps, Ct);

        var loaded = await store.LoadAsync(run.Id, Ct);

        Assert.NotNull(loaded);
        Assert.Equal([1, 2, 3], loaded.Steps.Select(s => s.Ordinal));
    }

    [Fact]
    public async Task PendingCall_round_trips_and_clears()
    {
        var store = NewStore();
        var run = NewRun();

        run.Status = RunStatus.WaitingForHuman;
        run.PendingCall = new PendingRefund("pay_7", "10.00", "EUR", "tok_123", "Refund 10.00 EUR?");

        await store.SaveAsync(run, [], Ct);

        var waiting = await store.LoadAsync(run.Id, Ct);

        Assert.NotNull(waiting);
        Assert.Equal(run.PendingCall, waiting.Run.PendingCall);

        // Clearing matters as much as saving. A pending refund left behind after the human
        // answered is a second refund waiting to happen.
        waiting.Run.PendingCall = null;
        waiting.Run.Status = RunStatus.Done;

        Assert.Equal(SaveOutcome.Saved, await store.SaveAsync(waiting.Run, [], Ct));

        var cleared = await store.LoadAsync(run.Id, Ct);

        Assert.NotNull(cleared);
        Assert.Null(cleared.Run.PendingCall);
    }

    [Fact]
    public async Task A_stale_save_is_a_conflict_not_an_overwrite()
    {
        var store = NewStore();
        var run = NewRun();

        await store.SaveAsync(run, [], Ct);

        // Two workers pick up the same run. Both hold the RowVer they loaded.
        var first = await store.LoadAsync(run.Id, Ct);
        var second = await store.LoadAsync(run.Id, Ct);

        Assert.NotNull(first);
        Assert.NotNull(second);

        first.Run.Answer = "the first worker";

        Assert.Equal(SaveOutcome.Saved, await store.SaveAsync(first.Run, [], Ct));

        second.Run.Answer = "the second worker";

        // The second worker lost the race. That is an answer the loop can act on, so no
        // exception escapes the store.
        Assert.Equal(SaveOutcome.Conflict, await store.SaveAsync(second.Run, [], Ct));

        var after = await store.LoadAsync(run.Id, Ct);

        Assert.NotNull(after);
        Assert.Equal("the first worker", after.Run.Answer);
    }

    private EfAgentRunStore NewStore()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlServer(db.ConnectionString)
            .Options;

        return new EfAgentRunStore(() => new AgentDbContext(options));
    }

    private static AgentRun NewRun() => new()
    {
        Id = Guid.NewGuid(),
        AgentName = "support-bot",
        ModelId = "test-model",
        PromptVersion = "v1",
        Status = RunStatus.Running,
        StepCount = 0,
        CreatedUtc = NowUtc(),
        UpdatedUtc = NowUtc()
    };

    private static AgentStep NewStep(Guid runId, int ordinal) => new()
    {
        Id = Guid.NewGuid(),
        RunId = runId,
        Ordinal = ordinal,
        Kind = "tool",
        ToolName = "get_payment",
        Outcome = "Ok",
        DurationMs = 12,
        CreatedUtc = NowUtc()
    };

    /// <summary>
    /// Now, cut to microseconds. The columns are <c>datetimeoffset(6)</c>, so a .NET
    /// timestamp with its full 100-nanosecond tick would come back rounded and the
    /// round-trip assert would fail for a reason that has nothing to do with the store.
    /// </summary>
    private static DateTimeOffset NowUtc()
    {
        var now = DateTimeOffset.UtcNow;

        return new DateTimeOffset(now.Ticks - (now.Ticks % 10), now.Offset);
    }
}
