using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
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
/// <para>
/// The last guard is the one about money: <b>"the model said so" is never an approval.</b> A
/// refund that comes back as a proposal stops the turn dead. The run is saved as
/// <see cref="RunStatus.WaitingForHuman"/> with the pending refund on it, a human is asked
/// through <see cref="IApprovalChannel"/>, the answer is saved <b>before</b> the money moves,
/// and only then does the host send the confirm. No model reply, and no tool result, can reach
/// that confirm.
/// </para>
/// </summary>
public sealed partial class AgentLoop(
    IChatClient model,
    IToolHost tools,
    IApprovalChannel approvals,
    IAgentRunStore store,
    AgentOptions options,
    TimeProvider clock)
{
    /// <summary>
    /// An amount in exactly the shape the tool contract allows. The ceiling can only be
    /// compared against a number, and only this shape is certainly a number. Anything else is
    /// the MCP server's fence to refuse: one owner per rule.
    /// </summary>
    [GeneratedRegex(@"^\d{1,7}(\.\d{1,2})?$")]
    private static partial Regex PlainAmount();

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
    /// Carries a saved run on from its last saved step. <b>Step 19 writes the body.</b>
    /// <para>
    /// The signature exists now so step 18's <c>ResumeTests</c> compile and fail as six red
    /// tests. A test file that cannot build is not a red test: it takes the whole project down
    /// with it, and the other green tests stop telling you anything.
    /// </para>
    /// </summary>
    public Task<AgentRun> ResumeAsync(Guid runId, CancellationToken ct)
        => throw new NotImplementedException("ResumeAsync is step 19.");

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
            PendingRefund? proposal = null;

            foreach (var call in calls)
            {
                if (proposal is not null)
                {
                    // A reply that proposes a refund and then asks for something else. The
                    // refund is about to go to a human, and the run can only wait for one
                    // answer, so everything after it is refused instead of queued.
                    steps.Add(Rejected(
                        run,
                        ++ordinal,
                        call,
                        "one_refund_at_a_time",
                        "A refund is already waiting for a human in this turn.",
                        "Wait for that answer. Ask for one refund at a time."));

                    continue;
                }

                var (step, found) = await RunCallAsync(run, call, ++ordinal, ct);

                steps.Add(step);

                proposal = found;

                // In a row, not in total. A budget on the total would kill a long, honest run
                // that hit two blips an hour apart.
                failures = step.Outcome == "Ok" ? 0 : failures + 1;
            }

            if (proposal is not null)
            {
                // Saved with the step that made it, in one write. The pending refund and its
                // step must land together, or a crash in between leaves one without the other.
                run.PendingCall = proposal;
                run.Status = RunStatus.WaitingForHuman;
            }

            await SaveAsync(run, steps, ct);

            if (proposal is not null)
            {
                return await ApproveAsync(run, proposal, ordinal, ct);
            }

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
    /// The human gate. The turn is over either way: the answer is written by the host, from the
    /// approval and the refund's own result, so no model reply stands between a yes and the money
    /// or dresses up a refusal as a success.
    /// </summary>
    private async Task<AgentRun> ApproveAsync(
        AgentRun run, PendingRefund refund, int ordinal, CancellationToken ct)
    {
        var approval = await approvals.AskAsync(run, refund, ct);

        // Saved before the confirm, on its own write. Crash in the window that opens next and
        // the run reads "approved by lk, no confirm after it", which step 19 can call
        // refund_in_doubt. Save it after the confirm and the same crash leaves a blank that
        // invites a second refund of the same money.
        var step = NewStep(run, ++ordinal);

        step.Kind = "approval";
        step.InputJson = Approved(refund);
        step.OutputJson = JsonSerializer.Serialize(new { approved = approval.Approved });
        step.Outcome = approval.Approved ? "Ok" : "Rejected";
        step.Approver = approval.By;

        await SaveAsync(run, [step], ct);

        return approval.Approved
            ? await ConfirmAsync(run, refund, approval.By, ordinal, ct)
            : await DoneAsync(
                run,
                $"No money moved. {approval.By} did not approve the refund of {refund.Amount} " +
                $"{refund.Currency} on payment {refund.PaymentId}.",
                ct);
    }

    /// <summary>The one call that moves money. It happens here and nowhere else.</summary>
    private async Task<AgentRun> ConfirmAsync(
        AgentRun run, PendingRefund refund, string approver, int ordinal, CancellationToken ct)
    {
        var started = clock.GetTimestamp();

        string outcome;
        string json;

        try
        {
            var result = await tools.ConfirmAsync(refund, ct);

            outcome = result.IsError ? "ToolError" : "Ok";
            json = result.Json;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // The type, never the message, the same as every other tool step.
            outcome = "ToolError";
            json = Error(
                "tool_threw",
                $"That tool failed with a {error.GetType().Name}.",
                "Tell the person the refund may or may not have happened, and stop.");
        }

        var step = NewStep(run, ++ordinal);

        step.Kind = "tool";
        step.ToolName = McpToolHost.RefundTool;
        step.InputJson = Approved(refund);
        step.OutputJson = json;
        step.Outcome = outcome;
        step.Approver = approver;
        step.DurationMs = (int)clock.GetElapsedTime(started).TotalMilliseconds;

        await SaveAsync(run, [step], ct);

        // The words are the host's, not the model's. "Refunded" is only ever said when the API
        // said so, and a refusal is never dressed up as a success.
        return await DoneAsync(
            run,
            outcome == "Ok"
                ? $"Refunded {refund.Amount} {refund.Currency} on payment {refund.PaymentId}. " +
                  $"Approved by {approver}."
                : $"No money moved. {approver} approved {refund.Amount} {refund.Currency} on " +
                  $"payment {refund.PaymentId}, but the payments API would not do it. Tell the " +
                  "person, and do not try again without a new approval.",
            ct);
    }

    /// <summary>
    /// One tool call the model asked for. Every way it can go wrong ends as a saved step and a
    /// result the model can read, never as an exception out of the loop.
    /// </summary>
    private async Task<(AgentStep Step, PendingRefund? Proposal)> RunCallAsync(
        AgentRun run, FunctionCallContent call, int ordinal, CancellationToken ct)
    {
        Dictionary<string, object?> args = call.Arguments is null ? [] : new(call.Arguments);

        var started = clock.GetTimestamp();

        string outcome;
        string json;
        PendingRefund? proposal = null;

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
        else if (HostRefusal(call.Name, args) is { } refusal)
        {
            // This agent's own rules, checked before the tool runs. A call refused here never
            // reaches the MCP server, so it cannot propose, cannot read and cannot move money.
            outcome = "Rejected";
            json = refusal;
        }
        else
        {
            try
            {
                var result = await tools.CallAsync(call.Name, args, ct);

                outcome = result.IsError ? "ToolError" : "Ok";
                json = result.Json;
                proposal = result.Proposal;
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

        var step = NewStep(run, ordinal);

        step.Kind = "tool";
        step.ToolName = call.Name;
        step.InputJson = JsonSerializer.Serialize(args, AIJsonUtilities.DefaultOptions);
        step.OutputJson = json;
        step.Outcome = outcome;
        step.DurationMs = (int)clock.GetElapsedTime(started).TotalMilliseconds;

        return (step, proposal);
    }

    /// <summary>
    /// This agent's own rules about a refund, checked before the tool is called. Null means the
    /// call may go through; a string is the refusal the model gets back.
    /// </summary>
    private string? HostRefusal(string tool, IReadOnlyDictionary<string, object?> args)
    {
        if (tool != McpToolHost.RefundTool)
        {
            return null;
        }

        if (args.TryGetValue("confirmation_token", out var written) &&
            !string.IsNullOrEmpty(written?.ToString()))
        {
            // The model has no token and cannot have one, so any token it sends is one it made
            // up or one it read somewhere it should not have. The server would refuse it as
            // token_unknown, which is safe but looks like an ordinary failed call. Refusing it
            // here makes it what it is: the model trying to approve money by itself.
            return Error(
                "confirmation_token_not_allowed",
                "You may not send a confirmation_token. You never receive one.",
                "Call refund_payment with payment_id, amount and currency only. A human is " +
                "asked, and this host sends the confirmation itself.");
        }

        if (args.TryGetValue("amount", out var asked) &&
            asked?.ToString() is { } text &&
            PlainAmount().IsMatch(text) &&
            decimal.Parse(text, CultureInfo.InvariantCulture) > options.RefundCeiling)
        {
            // The ceiling is this agent's limit, not the server's: another agent on the same
            // MCP server may be trusted with more. It is checked before the call, because a
            // proposal above the ceiling is a proposal a human might say yes to.
            return Error(
                "amount_above_ceiling",
                $"This agent may not refund more than " +
                $"{options.RefundCeiling.ToString("0.00", CultureInfo.InvariantCulture)} at once.",
                "Propose an amount at or below that limit, or tell the person that a refund " +
                "this big has to be done by a person.");
        }

        return null;
    }

    /// <summary>A call refused without being run, as a saved step.</summary>
    private AgentStep Rejected(
        AgentRun run,
        int ordinal,
        FunctionCallContent call,
        string error,
        string message,
        string hint)
    {
        var json = Error(error, message, hint);

        run.Messages.Add(new ChatMessage(
            ChatRole.Tool, [new FunctionResultContent(call.CallId, json)]));

        var step = NewStep(run, ordinal);

        step.Kind = "tool";
        step.ToolName = call.Name;
        step.InputJson = call.Arguments is null
            ? null
            : JsonSerializer.Serialize(call.Arguments, AIJsonUtilities.DefaultOptions);
        step.OutputJson = json;
        step.Outcome = "Rejected";

        return step;
    }

    /// <summary>The run is finished and the answer is written. Nothing is pending any more.</summary>
    private async Task<AgentRun> DoneAsync(AgentRun run, string answer, CancellationToken ct)
    {
        run.Status = RunStatus.Done;
        run.Answer = answer;
        run.PendingCall = null;

        await SaveAsync(run, [], ct);

        return run;
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

    /// <summary>
    /// A new step with everything the run already knows filled in. Provenance is not optional:
    /// six months later somebody asks which model and which prompt caused this, and a step
    /// without them can only shrug.
    /// </summary>
    private AgentStep NewStep(AgentRun run, int ordinal) => new()
    {
        Id = Guid.CreateVersion7(),
        RunId = run.Id,
        Ordinal = ordinal,
        ModelId = run.ModelId,
        PromptVersion = options.PromptVersion,
        CreatedUtc = clock.GetUtcNow(),
    };

    /// <summary>
    /// The refund a human saw, as JSON for a step. <b>Without the token.</b> A step's text is
    /// shown to the model and read by a reviewer, and the token is neither of their business.
    /// </summary>
    private static string Approved(PendingRefund refund) => JsonSerializer.Serialize(new
    {
        payment_id = refund.PaymentId,
        amount = refund.Amount,
        currency = refund.Currency,
    });

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
