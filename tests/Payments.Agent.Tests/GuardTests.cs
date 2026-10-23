using System.Net;
using Microsoft.Extensions.AI;
using Xunit;

namespace Payments.Agent.Tests;

/// <summary>
/// The thirteen guards, one weak-model mistake each. No trait: nothing here needs a database,
/// a model or a network, so CI runs them all.
/// <para>
/// Every one of these mistakes is real. A 4B model sends broken JSON, calls the same tool for
/// ever, prints a tool call as plain text and answers nothing at all. Each test writes down the
/// calm outcome the loop owes us: a saved run with a reason, never a hang and never a crash.
/// </para>
/// <para>
/// They are written against the <b>outside</b> of the loop: what the model was offered, what
/// reached the API, what the store holds. Never a private field. So they keep their meaning
/// when the loop is rewritten.
/// </para>
/// </summary>
public sealed class GuardTests
{
    private const string PaymentId = "0199c3f0-1a2b-7c3d-8e4f-5a6b7c8d9e0f";

    private const string OtherPaymentId = "0199c3f0-1a2b-7c3d-8e4f-5a6b7c8d9e10";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_step_cap_takes_the_tools_away()
    {
        await using var harness = await AgentHarness.StartAsync();

        // A different limit every pass, so the repeat breaker stays quiet and the cap is the
        // only guard under test. Two guards in one test cannot tell you which one fired.
        for (var n = 1; n <= 6; n++)
        {
            harness.Api.Enqueue(HttpStatusCode.OK, EmptyList);
            harness.Model.Calls("list_payments", List(limit: n));
        }

        harness.Model.Says("I could not find it, sorry.");

        var run = await harness.Loop.StartAsync("find that payment", Ct);

        // Six passes, then the tools come off. Without the cap a model that always calls a
        // tool never stops, and the bill never stops either.
        Assert.Equal(6, harness.Api.Requests.Count);
        Assert.Equal(7, harness.Model.Requests.Count);
        Assert.Empty(harness.Model.Requests[6].ToolsOffered);

        // The tools are gone, so the model has to answer in words. That answer is the run's.
        Assert.Equal(RunStatus.Done, run.Status);
        Assert.Equal("I could not find it, sorry.", run.Answer);
        Assert.Equal(RunStatus.Done, harness.Store.Saved(run.Id).Run.Status);
    }

    [Fact]
    public async Task A_tool_call_after_the_cap_fails_the_run()
    {
        await using var harness = await AgentHarness.StartAsync();

        for (var n = 1; n <= 6; n++)
        {
            harness.Api.Enqueue(HttpStatusCode.OK, EmptyList);
            harness.Model.Calls("list_payments", List(limit: n));
        }

        // The pass with no tools at all, and the model asks for a tool anyway.
        harness.Model.Calls("list_payments", List(limit: 7));

        var run = await harness.Loop.StartAsync("find that payment", Ct);

        // The cap is the one bound that cannot be dodged. A model that asks for a tool it was
        // not offered gets no seventh call: the run stops with a reason a human can read.
        Assert.Equal(6, harness.Api.Requests.Count);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("step_cap_ignored", run.Reason);
        Assert.Equal("step_cap_ignored", harness.Store.Saved(run.Id).Run.Reason);
    }

    [Fact]
    public async Task Three_identical_calls_trip_the_repeat_breaker()
    {
        await using var harness = await AgentHarness.StartAsync();

        for (var n = 1; n <= 3; n++)
        {
            harness.Api.Enqueue(HttpStatusCode.OK, StubApi.Payment(PaymentId));
            harness.Model.Calls("get_payment", Get(PaymentId));
        }

        harness.Model.Says("That payment is captured, 80.00 EUR.");

        var run = await harness.Loop.StartAsync("read that payment", Ct);

        // Three identical calls is a stuck model, not research. The answer cannot change, so
        // the fourth call is pure waste: the tools come off before it happens.
        Assert.Equal(3, harness.Api.Requests.Count);
        Assert.Equal(4, harness.Model.Requests.Count);
        Assert.Empty(harness.Model.Requests[3].ToolsOffered);
        Assert.Equal(RunStatus.Done, run.Status);
    }

    [Fact]
    public async Task Two_tools_taking_turns_slip_past_the_breaker_but_not_the_cap()
    {
        await using var harness = await AgentHarness.StartAsync();

        // list, get, list, get, list, get. Same arguments every time, so each call is useless,
        // but no call is ever the third identical one in a row.
        for (var n = 1; n <= 3; n++)
        {
            harness.Api.Enqueue(HttpStatusCode.OK, EmptyList);
            harness.Model.Calls("list_payments", List(limit: 20));

            harness.Api.Enqueue(HttpStatusCode.OK, StubApi.Payment(PaymentId));
            harness.Model.Calls("get_payment", Get(PaymentId));
        }

        harness.Model.Says("Captured, 80.00 EUR.");

        var run = await harness.Loop.StartAsync("check it again and again", Ct);

        // This is QA Q4. Two tools taking turns walk past the repeat breaker every time, so
        // the breaker alone is not a bound. The cap is, and it stops this at six.
        Assert.Equal(6, harness.Api.Requests.Count);
        Assert.Equal(7, harness.Model.Requests.Count);
        Assert.Empty(harness.Model.Requests[6].ToolsOffered);
        Assert.Equal(RunStatus.Done, run.Status);
    }

    [Fact]
    public async Task Three_failures_in_a_row_spend_the_failure_budget()
    {
        await using var harness = await AgentHarness.StartAsync();

        for (var n = 1; n <= 3; n++)
        {
            harness.Api.Enqueue(HttpStatusCode.InternalServerError);
            harness.Model.Calls("get_payment", Get(PaymentId));
        }

        // Nothing is scripted after the third reply. If the loop asks a fourth time the fake
        // model throws, and the test says so out loud instead of passing by luck.

        var run = await harness.Loop.StartAsync("read that payment", Ct);

        Assert.Equal(3, harness.Api.Requests.Count);
        Assert.Equal(3, harness.Model.Requests.Count);

        var saved = harness.Store.Saved(run.Id);

        // Three errors in a row means the thing on the other side is down. Asking the model to
        // try a fourth time only spends money on the same failure.
        Assert.Equal(3, saved.Steps.Count(s => s.Outcome == "ToolError"));
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("tool_failure_budget", run.Reason);
        Assert.Equal("tool_failure_budget", saved.Run.Reason);
    }

    [Fact]
    public async Task A_success_in_between_resets_the_budget()
    {
        await using var harness = await AgentHarness.StartAsync();

        // Fail, fail, succeed, fail, fail. Five calls and four failures, but never three in
        // a row, so the budget must never be spent.
        foreach (var ok in new[] { false, false, true, false, false })
        {
            harness.Api.Enqueue(
                ok ? HttpStatusCode.OK : HttpStatusCode.InternalServerError,
                ok ? StubApi.Payment(PaymentId) : null);

            harness.Model.Calls("get_payment", Get(PaymentId));
        }

        harness.Model.Says("It is captured, but the service is flaky today.");

        RunStatus? statusAfterFive = null;

        // The run can only end Done or Failed, so "still Running after the fifth step" has to
        // be read from inside the loop, on the sixth model call.
        harness.Model.OnRequest = n =>
        {
            if (n == 6)
            {
                statusAfterFive = harness.Store.Only()?.Run.Status;
            }
        };

        var run = await harness.Loop.StartAsync("read that payment", Ct);

        // The budget counts failures in a row, not failures in total. A budget that counted
        // the total would kill a long, honest run that hit two blips an hour apart.
        Assert.Equal(RunStatus.Running, statusAfterFive);
        Assert.Equal(5, harness.Api.Requests.Count);
        Assert.Equal(RunStatus.Done, run.Status);
        Assert.Null(run.Reason);
    }

    [Fact]
    public async Task An_unknown_tool_is_refused_not_thrown()
    {
        await using var harness = await AgentHarness.StartAsync();

        harness.Model
            .Calls("delete_payment", Get(PaymentId))
            .Says("I cannot delete a payment.");

        var run = await harness.Loop.StartAsync("delete that payment", Ct);

        // A name the server does not have must never reach the server, and must never crash
        // the loop either. The model made it up, so the model is told the real names.
        Assert.Empty(harness.Api.Requests);

        var step = Assert.Single(harness.Store.Saved(run.Id).Steps);

        Assert.Equal("Rejected", step.Outcome);
        Assert.Equal("delete_payment", step.ToolName);

        var result = Assert.Single(harness.Model.Requests[1].Messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionResultContent>());

        var text = result.Result?.ToString() ?? "";

        // A refusal that does not say what is allowed teaches the model nothing, and it
        // guesses a second wrong name.
        Assert.Contains("get_payment", text);
        Assert.Contains("list_payments", text);
        Assert.Contains("refund_payment", text);

        Assert.Equal(RunStatus.Done, run.Status);
    }

    [Fact]
    public async Task Broken_json_arguments_are_refused_not_repaired()
    {
        await using var harness = await AgentHarness.StartAsync();

        harness.Model
            .BrokenArgs("refund_payment")
            .Says("I could not read my own refund request, so I refunded nothing.");

        var run = await harness.Loop.StartAsync("refund that payment", Ct);

        // Refuse, never repair. Guessing the missing brace means guessing an amount, and a
        // guessed amount on a refund is somebody else's money.
        Assert.Empty(harness.Api.Requests);

        var step = Assert.Single(harness.Store.Saved(run.Id).Steps);

        Assert.Equal("Rejected", step.Outcome);
        Assert.Equal("refund_payment", step.ToolName);
        Assert.Contains("invalid_arguments", step.OutputJson);
    }

    [Fact]
    public async Task An_empty_reply_is_retried_once_then_fails()
    {
        // Twice nothing: one retry, then the run stops with a reason.
        await using (var silent = await AgentHarness.StartAsync())
        {
            silent.Model.Empty().Empty();

            var run = await silent.Loop.StartAsync("when do payments settle?", Ct);

            // One retry, not a retry loop: a model that answers nothing twice will answer
            // nothing a hundred times, and every try costs.
            Assert.Equal(2, silent.Model.Requests.Count);
            Assert.Equal(RunStatus.Failed, run.Status);
            Assert.Equal("empty_reply", run.Reason);
            Assert.Equal("empty_reply", silent.Store.Saved(run.Id).Run.Reason);
        }

        // Nothing, then an answer. One blank reply is a hiccup, so the retry has to be real.
        await using var recovers = await AgentHarness.StartAsync();

        recovers.Model.Empty().Says("The next working day.");

        var second = await recovers.Loop.StartAsync("when do payments settle?", Ct);

        Assert.Equal(2, recovers.Model.Requests.Count);
        Assert.Equal(RunStatus.Done, second.Status);
        Assert.Equal("The next working day.", second.Answer);
    }

    [Fact]
    public async Task A_tool_call_printed_as_text_is_never_run()
    {
        await using var harness = await AgentHarness.StartAsync();

        var leak =
            "{\"name\":\"refund_payment\",\"arguments\":{\"payment_id\":\"" + PaymentId +
            "\",\"amount\":\"80.00\",\"currency\":\"EUR\"}}";

        harness.Model.Leaks(leak).Leaks(leak);

        var run = await harness.Loop.StartAsync("refund that payment", Ct);

        // This is the dangerous one. Text that looks like a tool call is text: nobody parses
        // it and nobody runs it. Aperio recovers some of these, and for a refund tool that is
        // a risk this lab does not take.
        Assert.Empty(harness.Api.Requests);
        Assert.Equal(2, harness.Model.Requests.Count);

        var first = harness.Model.Requests[0].Messages;
        var second = harness.Model.Requests[1].Messages;

        // The leaked reply, plus a note the loop added. Without the note the model has no
        // reason to do anything different on the retry.
        Assert.Equal(first.Count + 2, second.Count);

        var note = second[^1];

        Assert.NotEqual(ChatRole.Assistant, note.Role);
        Assert.Contains("tool", note.Text, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("tool_call_leak", run.Reason);
        Assert.Equal("tool_call_leak", harness.Store.Saved(run.Id).Run.Reason);
    }

    [Fact]
    public async Task A_tool_that_throws_is_a_ToolError_step_not_a_crash()
    {
        ThrowingToolHost? broken = null;

        await using var harness = await AgentHarness.StartAsync(
            wrapTools: inner => broken = new ThrowingToolHost(
                inner, "get_payment", () => new IOException("the pipe to the MCP server is gone")));

        harness.Model
            .Calls("get_payment", Get(PaymentId))
            .Says("I could not read that payment just now.");

        var run = await harness.Loop.StartAsync("read that payment", Ct);

        Assert.Equal(1, broken!.Throws);

        var step = Assert.Single(harness.Store.Saved(run.Id).Steps);

        Assert.Equal("ToolError", step.Outcome);

        // The type, never the message. A message can carry a connection string, a host name
        // or a card number, and everything on a step is shown to the model.
        Assert.Contains(nameof(IOException), step.OutputJson);
        Assert.DoesNotContain("pipe to the MCP server", step.OutputJson);

        // A throw is not the end of the turn. The loop tells the model and carries on.
        Assert.Equal(2, harness.Model.Requests.Count);
        Assert.Equal(RunStatus.Done, run.Status);
    }

    [Fact]
    public async Task Cancel_saves_the_run_as_Failed_cancelled()
    {
        await using var harness = await AgentHarness.StartAsync();

        using var caller = new CancellationTokenSource();

        harness.Model.Slow(TimeSpan.FromMinutes(1));

        // Cancel while the model is thinking. Pressing Ctrl+C lands exactly here, because
        // waiting for the model is where a turn spends almost all of its time.
        harness.Model.OnRequest = _ => caller.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Loop.StartAsync("read that payment", caller.Token));

        var saved = harness.Store.Only();

        // Saved, not lost. The save uses a fresh token, or the cancel that stopped the run
        // would stop the write that records it, and the run would vanish.
        Assert.NotNull(saved);
        Assert.Equal(RunStatus.Failed, saved.Run.Status);
        Assert.Equal("cancelled", saved.Run.Reason);
    }

    [Fact(Timeout = 10_000)]
    public async Task The_turn_deadline_stops_a_slow_model()
    {
        var options = new AgentOptions
        {
            AgentName = "support-bot",
            TurnDeadline = TimeSpan.FromMinutes(5),
        };

        await using var harness = await AgentHarness.StartAsync(options);

        harness.Model.Slow(TimeSpan.FromMinutes(10));

        // Fake time, so the test takes milliseconds. A test that really waited five minutes
        // would be deleted by the first person in a hurry, and the guard would go untested.
        harness.Model.OnRequest = _ => harness.Clock.Advance(TimeSpan.FromMinutes(5));

        // TestContext.Current.CancellationToken in full, not the Ct shortcut: the analyzer
        // wants to see it in a test that has a Timeout (xUnit1069).
        var run = await harness.Loop.StartAsync(
            "read that payment", TestContext.Current.CancellationToken);

        // The step cap bounds passes, not seconds. One model call that never comes back sits
        // for ever under a step cap alone. This is the guard Aperio does not have (CF-007).
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("turn_deadline", run.Reason);

        var saved = harness.Store.Saved(run.Id);

        Assert.Equal(RunStatus.Failed, saved.Run.Status);
        Assert.Equal("turn_deadline", saved.Run.Reason);
    }

    /// <summary>A <c>GET /payments/{id}</c> call's arguments.</summary>
    private static Dictionary<string, object?> Get(string paymentId) =>
        new() { ["payment_id"] = paymentId };

    /// <summary>A <c>list_payments</c> call's arguments.</summary>
    private static Dictionary<string, object?> List(int limit) =>
        new() { ["limit"] = limit };

    /// <summary>A list answer with nothing in it. These tests count calls, not rows.</summary>
    private static object EmptyList => new { items = Array.Empty<object>(), truncated = false };
}
