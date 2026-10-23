using System.Text.Json;
using Microsoft.Extensions.AI;
using Payments.Agent.Storage;

namespace Payments.Agent.Tests;

/// <summary>
/// The run store as a dictionary. No database, so the loop tests run in CI.
/// <para>
/// It copies on save and on load. Without the copy a test could change a "saved" run by
/// holding on to the object it passed in, and the loop could read its own uncommitted work.
/// Neither happens with a real database, so neither may happen here.
/// </para>
/// <para>
/// It also hands out versions, so losing a race is a <see cref="SaveOutcome.Conflict"/> here
/// too. A fake store that always says Saved would let the loop pass a test the real store fails.
/// </para>
/// </summary>
public sealed class InMemoryAgentRunStore : IAgentRunStore
{
    private readonly Dictionary<Guid, (AgentRun Run, List<AgentStep> Steps)> _rows = [];

    private readonly object _gate = new();

    private int _version;

    public int Saves { get; private set; }

    public Task<LoadedRun?> LoadAsync(Guid id, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!_rows.TryGetValue(id, out var row))
            {
                return Task.FromResult<LoadedRun?>(null);
            }

            return Task.FromResult<LoadedRun?>(new LoadedRun(
                Copy(row.Run),
                [.. row.Steps.OrderBy(s => s.Ordinal).Select(Copy)]));
        }
    }

    public Task<SaveOutcome> SaveAsync(
        AgentRun run,
        IReadOnlyList<AgentStep> newSteps,
        CancellationToken ct)
    {
        lock (_gate)
        {
            var isNew = !_rows.TryGetValue(run.Id, out var row);

            // The same rule a rowversion gives us: the version you read has to be the version
            // still in the row, or somebody else saved in between.
            if (!isNew && !row.Run.RowVer.SequenceEqual(run.RowVer))
            {
                return Task.FromResult(SaveOutcome.Conflict);
            }

            run.RowVer = BitConverter.GetBytes(++_version);

            List<AgentStep> steps = isNew ? [] : row.Steps;
            steps.AddRange(newSteps.Select(Copy));

            _rows[run.Id] = (Copy(run), steps);

            Saves++;

            return Task.FromResult(SaveOutcome.Saved);
        }
    }

    /// <summary>What the store holds right now, with no copy of the steps list to order.</summary>
    public LoadedRun Saved(Guid id) => LoadAsync(id, CancellationToken.None).Result!;

    /// <summary>
    /// The one run the store holds, or null while it holds none. A test that watches from
    /// inside the loop needs this: the loop makes the run id, so the test cannot know it yet.
    /// </summary>
    public LoadedRun? Only()
    {
        lock (_gate)
        {
            return _rows.Count == 0 ? null : Saved(_rows.Keys.Single());
        }
    }

    private static AgentRun Copy(AgentRun run) => new()
    {
        Id = run.Id,
        AgentName = run.AgentName,
        ModelId = run.ModelId,
        PromptVersion = run.PromptVersion,
        Status = run.Status,
        StepCount = run.StepCount,

        // The messages hold AIContent, so a shallow copy of the list would still share the
        // tool calls inside it. JSON is the only copy that goes all the way down, and these
        // are the options that know every content type.
        Messages = JsonSerializer.Deserialize<List<ChatMessage>>(
            JsonSerializer.Serialize(run.Messages, AIJsonUtilities.DefaultOptions),
            AIJsonUtilities.DefaultOptions)!,

        PendingCall = run.PendingCall,
        Answer = run.Answer,
        Reason = run.Reason,
        CreatedUtc = run.CreatedUtc,
        UpdatedUtc = run.UpdatedUtc,
        RowVer = [.. run.RowVer],
    };

    private static AgentStep Copy(AgentStep step) => new()
    {
        Id = step.Id,
        RunId = step.RunId,
        Ordinal = step.Ordinal,
        Kind = step.Kind,
        ToolName = step.ToolName,
        InputJson = step.InputJson,
        OutputJson = step.OutputJson,
        Outcome = step.Outcome,
        ModelId = step.ModelId,
        PromptVersion = step.PromptVersion,
        Approver = step.Approver,
        DurationMs = step.DurationMs,
        CreatedUtc = step.CreatedUtc,
    };
}
