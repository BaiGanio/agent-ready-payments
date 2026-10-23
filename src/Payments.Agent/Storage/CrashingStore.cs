namespace Payments.Agent.Storage;

/// <summary>
/// Stops the process on purpose, to make a real crash for the resume by hand. It counts saved
/// steps, and it dies <b>after</b> the save: a crash before the write would lose the step, and
/// then the resume would only be tested against a shorter history.
/// </summary>
public sealed class CrashingStore(IAgentRunStore inner, int after) : IAgentRunStore
{
    private int _saved;

    public Task<LoadedRun?> LoadAsync(Guid id, CancellationToken ct) => inner.LoadAsync(id, ct);

    public async Task<SaveOutcome> SaveAsync(
        AgentRun run, IReadOnlyList<AgentStep> newSteps, CancellationToken ct)
    {
        var outcome = await inner.SaveAsync(run, newSteps, ct);

        _saved += newSteps.Count;

        if (_saved >= after)
        {
            Console.Error.WriteLine($"[crash] {_saved} steps saved. Dying on purpose.");

            Environment.Exit(99);
        }

        return outcome;
    }
}
