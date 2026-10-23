namespace Payments.Agent.Storage;

/// <summary>What a save did. Losing a race is an answer, not a crash.</summary>
public enum SaveOutcome
{
    Saved,
    Conflict
}

/// <summary>A run and its steps, the steps in <c>Ordinal</c> order.</summary>
public sealed record LoadedRun(AgentRun Run, IReadOnlyList<AgentStep> Steps);

/// <summary>
/// The notepad the loop writes to. One save per pass of the loop, so a process that dies
/// mid-run leaves a row another process can pick up.
/// </summary>
public interface IAgentRunStore
{
    Task<LoadedRun?> LoadAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Saves the run and inserts <paramref name="newSteps"/>, together, as one unit.
    /// <para>
    /// This <b>returns</b> a conflict instead of throwing one. Another worker saving first is
    /// a normal answer for a run store, and the loop has to stop calmly, not crash.
    /// </para>
    /// </summary>
    Task<SaveOutcome> SaveAsync(AgentRun run, IReadOnlyList<AgentStep> newSteps, CancellationToken ct);
}
