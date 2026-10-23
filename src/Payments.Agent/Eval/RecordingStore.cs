using Payments.Agent.Storage;

namespace Payments.Agent.Eval;

/// <summary>
/// Saves to the real store and keeps a copy of every step in memory, in order.
/// <para>
/// It is a decorator, like <see cref="PrintingStore"/>, for the same reason: what the scoring
/// reads is exactly what was written to the database. Scoring from the loop's own return value
/// would score a run that nobody can look up afterwards, and the first question about a bad
/// score is always "show me that run".
/// </para>
/// <para>
/// One instance per scenario, so <see cref="Steps"/> is that scenario's run and nothing else.
/// </para>
/// </summary>
public sealed class RecordingStore(IAgentRunStore inner) : IAgentRunStore
{
    private readonly List<AgentStep> _steps = [];

    /// <summary>Every saved step, in the order it was written.</summary>
    public IReadOnlyList<AgentStep> Steps => _steps;

    public Task<LoadedRun?> LoadAsync(Guid id, CancellationToken ct) => inner.LoadAsync(id, ct);

    public async Task<SaveOutcome> SaveAsync(
        AgentRun run, IReadOnlyList<AgentStep> newSteps, CancellationToken ct)
    {
        var outcome = await inner.SaveAsync(run, newSteps, ct);

        // Only what the inner store accepted. A step kept here after a lost race would be a step
        // in the score that is in no table.
        if (outcome == SaveOutcome.Saved)
        {
            _steps.AddRange(newSteps);
        }

        return outcome;
    }
}
