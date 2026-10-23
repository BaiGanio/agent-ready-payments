using System.Text.Json;
using Microsoft.Extensions.AI;
using Payments.Agent.Storage;
using Payments.Agent.Tools;

namespace Payments.Agent;

/// <summary>
/// The agent loop: ask the model, run the tool it asked for, feed the result back, save, repeat.
/// <para>
/// Everything it touches is an interface, so the same loop runs against <c>llama-server</c> and
/// against a scripted fake, and the tests never need a real model.
/// </para>
/// <para>
/// Most of this file is guards. A small local model sends broken JSON, calls the same tool for
/// ever, prints a tool call as plain text and sometimes answers nothing at all. Each guard turns
/// one of those mistakes into a saved run with a reason, instead of a hang, a crash or a wrong
/// action. The rule throughout is <b>refuse, never repair</b>: the loop never guesses what the
/// model meant.
/// </para>
/// </summary>
public sealed class AgentLoop(
    IChatClient model,
    IToolHost tools,
    IApprovalChannel approvals,
    IAgentRunStore store,
    AgentOptions options,
    TimeProvider clock)
{
    /// <summary>Starts a new run from one human request and carries it as far as it goes.</summary>
    public async Task<AgentRun> StartAsync(string request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        var run = new AgentRun
        {
            Id = Guid.CreateVersion7(),
            AgentName = options.AgentName,
            PromptVersion = options.PromptVersion,
            Status = RunStatus.Running,
            Messages =
            [
                new ChatMessage(ChatRole.System, options.SystemPrompt),
                new ChatMessage(ChatRole.User, request),
            ],
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        await SaveAsync(run, [], ct);

        return await RunAsync(run, 0, ct);
    }

    /// <summary>
    /// One turn, with a wall clock around it. The step cap bounds passes, not seconds: one model
    /// call that never comes back would sit for ever under a step cap alone (<c>CF-007</c>).
    /// </summary>
    private async Task<AgentRun> RunAsync(AgentRun run, int ordinal, CancellationToken ct)
    {
        // The deadline reads the loop's TimeProvider, so a test advances fake time and
        // production uses the real clock. The linked token means either the caller or the
        // deadline can stop the turn, and the loop only has one token to pass down.
        using var turn = new CancellationTokenSource(options.TurnDeadline, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, turn.Token);

        try
        {
            return await PassesAsync(run, ordinal, linked.Token);
        }
        catch (OperationCanceledException) when (turn.IsCancellationRequested)
        {
            // Our own decision, not the caller's. The caller asked for an answer, so it gets
            // the run back with the reason on it.
            return await StopAsync(run, "turn_deadline");
        }
        catch (OperationCanceledException)
        {
            // The caller cancelled. Save first, then let the cancel through: a caller that
            // asked to stop must see a cancel, not a tidy answer.
            await StopAsync(run, "cancelled");

            throw;
        }
    }

    private async Task<AgentRun> PassesAsync(AgentRun run, int ordinal, CancellationToken ct)
    {
        var toolsOff = false;
        var failures = 0;
        var empties = 0;
        var leaks = 0;
        var repeats = 0;
        string? lastPass = null;

        while (run.Status == RunStatus.Running)
        {
            var reply = await model.GetResponseAsync(run.Messages, Offer(toolsOff), ct);

            run.ModelId = reply.ModelId;
            run.Messages.AddRange(reply.Messages);

            var calls = reply.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionCallContent>()
                .ToList();

            if (calls.Count == 0)
            {
                if (string.IsNullOrWhiteSpace(reply.Text))
                {
                    // One blank reply is a hiccup, two is a broken model. Retrying for ever
                    // costs money and never ends.
                    if (++empties >= 2)
                    {
                        return await StopAsync(run, "empty_reply");
                    }

                    continue;
                }

                if (Leaked(reply.Text))
                {
                    // Text that looks like a tool call is text. Nobody parses it and nobody
                    // runs it: for a refund tool, a "helpful" repair moves real money.
                    if (++leaks >= 2)
                    {
                        return await StopAsync(run, "tool_call_leak");
                    }

                    run.Messages.Add(Note(
                        "You printed a tool call as text. Text is never run. Ask for the tool " +
                        "itself, or answer in plain words."));

                    continue;
                }

                run.Status = RunStatus.Done;
                run.Answer = reply.Text;

                await SaveAsync(run, [], ct);

                break;
            }

            if (toolsOff)
            {
                // The one bound that cannot be dodged. The model was offered no tools at all,
                // so a tool call here is the model ignoring the only limit left.
                return await StopAsync(run, "step_cap_ignored");
            }

            empties = 0;
            leaks = 0;
            run.StepCount++;

            List<AgentStep> steps = [];

            foreach (var call in calls)
            {
                var step = await RunCallAsync(run, call, ++ordinal, reply.ModelId, ct);

                steps.Add(step);

                // In a row, not in total. A budget on the total would kill a long, honest run
                // that hit two blips an hour apart.
                failures = step.Outcome == "Ok" ? 0 : failures + 1;
            }

            await SaveAsync(run, steps, ct);

            if (failures >= options.FailureBudget)
            {
                // Three errors in a row means the thing on the other side is down. Another
                // model call only spends money on the same failure.
                return await StopAsync(run, "tool_failure_budget");
            }

            var pass = Fingerprint(steps);
            repeats = pass == lastPass ? repeats + 1 : 1;
            lastPass = pass;

            if (repeats >= options.RepeatLimit || run.StepCount >= options.StepCap)
            {
                // The tools come off and the model has to answer in words. Two tools taking
                // turns walk past the repeat breaker every time, so the cap is the real bound.
                toolsOff = true;

                run.Messages.Add(Note(repeats >= options.RepeatLimit
                    ? "You called the same tool with the same arguments too many times. The " +
                      "answer will not change. The tools are gone: answer in plain words now."
                    : "You have used all the tool calls this turn allows. The tools are gone: " +
                      "answer in plain words with what you already know."));
            }
        }

        return run;
    }

    /// <summary>
    /// One tool call the model asked for. Every way it can go wrong ends as a saved step and a
    /// result the model can read, never as an exception out of the loop.
    /// </summary>
    private async Task<AgentStep> RunCallAsync(
        AgentRun run, FunctionCallContent call, int ordinal, string? modelId, CancellationToken ct)
    {
        Dictionary<string, object?> args = call.Arguments is null ? [] : new(call.Arguments);

        var started = clock.GetTimestamp();

        string outcome;
        string json;

        if (!tools.Tools.Any(t => t.Name == call.Name))
        {
            // The model made the name up. A refusal that does not say what is allowed teaches
            // it nothing, so the real names go back with it.
            outcome = "Rejected";
            json = Error(
                "unknown_tool",
                $"There is no tool called {call.Name}.",
                $"The tools are: {string.Join(", ", tools.Tools.Select(t => t.Name))}.");
        }
        else if (call.Exception is not null)
        {
            // Refuse, never repair. Guessing the missing brace means guessing an amount, and a
            // guessed amount on a refund is somebody else's money.
            outcome = "Rejected";
            json = Error(
                "invalid_arguments",
                "The arguments of that call were not valid JSON, so it was not run.",
                "Send the call again with valid JSON arguments. Nothing was repaired for you.");
        }
        else
        {
            try
            {
                var result = await tools.CallAsync(call.Name, args, ct);

                outcome = result.IsError ? "ToolError" : "Ok";
                json = result.Json;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // The type, never the message. A message can carry a connection string, a host
                // name or a card number, and everything on a step is shown to the model.
                outcome = "ToolError";
                json = Error(
                    "tool_threw",
                    $"That tool failed with a {error.GetType().Name}.",
                    "Try once more. If it fails again, tell the person and stop.");
            }
        }

        // The result goes back whatever happened, with the call id that threads it to the ask.
        run.Messages.Add(new ChatMessage(
            ChatRole.Tool, [new FunctionResultContent(call.CallId, json)]));

        return new AgentStep
        {
            Id = Guid.CreateVersion7(),
            RunId = run.Id,
            Ordinal = ordinal,
            Kind = "tool",
            ToolName = call.Name,
            InputJson = JsonSerializer.Serialize(args, AIJsonUtilities.DefaultOptions),
            OutputJson = json,
            Outcome = outcome,
            ModelId = modelId,
            PromptVersion = options.PromptVersion,
            DurationMs = (int)clock.GetElapsedTime(started).TotalMilliseconds,
            CreatedUtc = clock.GetUtcNow(),
        };
    }

    /// <summary>
    /// A guard that ends the turn. The save uses <see cref="CancellationToken.None"/>: the token
    /// that stopped the run must not also stop the write that records it, or the run vanishes.
    /// </summary>
    private async Task<AgentRun> StopAsync(AgentRun run, string reason)
    {
        run.Status = RunStatus.Failed;
        run.Reason = reason;

        await SaveAsync(run, [], CancellationToken.None);

        return run;
    }

    /// <summary>What the model is offered. Nothing at all, once a guard has taken the tools.</summary>
    private ChatOptions Offer(bool toolsOff) =>
        toolsOff ? new ChatOptions() : new ChatOptions { Tools = [.. tools.Tools] };

    /// <summary>
    /// A tool call printed as words: a real tool name, with a brace or a bracket after it. That
    /// is what a 4B model's leak looks like, and it is never close enough to parse.
    /// </summary>
    private bool Leaked(string text) =>
        tools.Tools.Any(t =>
        {
            var at = text.IndexOf(t.Name, StringComparison.Ordinal);

            return at >= 0 && text.IndexOfAny(['{', '('], at) > at;
        });

    /// <summary>The whole pass as one string: the same tools with the same arguments, or not.</summary>
    private static string Fingerprint(IEnumerable<AgentStep> steps) =>
        string.Join("|", steps.Select(s => $"{s.ToolName}:{s.InputJson}"));

    /// <summary>A word from the host to the model. Not an answer, and not a tool result.</summary>
    private static ChatMessage Note(string text) => new(ChatRole.User, text);

    /// <summary>The same error shape the MCP tools use, so the model reads one shape only.</summary>
    private static string Error(string error, string message, string hint) =>
        JsonSerializer.Serialize(new { error, message, hint });

    private async Task SaveAsync(AgentRun run, IReadOnlyList<AgentStep> steps, CancellationToken ct)
    {
        run.UpdatedUtc = clock.GetUtcNow();

        if (await store.SaveAsync(run, steps, ct) == SaveOutcome.Conflict)
        {
            throw new InvalidOperationException(
                $"Run {run.Id} was saved by somebody else. This loop no longer owns it.");
        }
    }
}
