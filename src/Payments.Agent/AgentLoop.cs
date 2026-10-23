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

    private async Task<AgentRun> RunAsync(AgentRun run, int ordinal, CancellationToken ct)
    {   
        var offered = new ChatOptions { Tools = [.. tools.Tools] };
        while (run.Status == RunStatus.Running)
        {            
            var reply = await model.GetResponseAsync(run.Messages, offered, ct);
            run.ModelId = reply.ModelId;
            run.Messages.AddRange(reply.Messages);

            var calls = reply.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionCallContent>()
                .ToList();

            if (calls.Count == 0)
            {
                run.Status = RunStatus.Done;
                run.Answer = reply.Text;

                await SaveAsync(run, [], ct);

                break;
            }

            run.StepCount++;
            List<AgentStep> steps = [];
            foreach (var call in calls)
            {
                Dictionary<string, object?> args =
                    call.Arguments is null ? [] : new(call.Arguments);

                var started = clock.GetTimestamp();

                ToolOutcome outcome = await tools.CallAsync(call.Name, args, ct);              
                run.Messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, outcome.Json)]));

                steps.Add(new AgentStep
                {
                    Id = Guid.CreateVersion7(),
                    RunId = run.Id,
                    Ordinal = ++ordinal,
                    Kind = "tool",
                    ToolName = call.Name,
                    InputJson = JsonSerializer.Serialize(args, AIJsonUtilities.DefaultOptions),
                    OutputJson = outcome.Json,
                    Outcome = outcome.IsError ? "ToolError" : "Ok",
                    ModelId = reply.ModelId,
                    PromptVersion = options.PromptVersion,
                    DurationMs = (int)clock.GetElapsedTime(started).TotalMilliseconds,
                    CreatedUtc = clock.GetUtcNow(),
                });
            }

            await SaveAsync(run, steps, ct);   
        }
        return run;
    }

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
