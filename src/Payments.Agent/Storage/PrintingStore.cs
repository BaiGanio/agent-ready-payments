namespace Payments.Agent.Storage;

/// <summary>
/// Shows a run while it happens: the run id on its first save, the model id once the first
/// reply names it, then one line per saved step.
/// <para>
/// It is a <b>decorator</b>, not a branch inside the loop. Every line it prints is a line that
/// was really written to the database, so the demo on screen and the evidence in the table
/// cannot disagree. A loop with an "if demo, print" branch would prove nothing, because the
/// printing could drift away from the saving.
/// </para>
/// <para>
/// The id is printed from here and not from the caller because the run is created inside
/// <c>StartAsync</c>. It lands on the first save, which happens before any model call, so the
/// id is on screen before anything slow or anything that can crash.
/// </para>
/// </summary>
public sealed class PrintingStore(IAgentRunStore inner) : IAgentRunStore
{
    /// <summary>
    /// Which run has had its header printed. It is the <b>id</b>, not a bool, because in
    /// <c>chat</c> one decorator serves many runs: every line the human types is a new run and
    /// needs its own header.
    /// </summary>
    private Guid _announced;

    private bool _named;

    public Task<LoadedRun?> LoadAsync(Guid id, CancellationToken ct) => inner.LoadAsync(id, ct);

    public async Task<SaveOutcome> SaveAsync(
        AgentRun run, IReadOnlyList<AgentStep> newSteps, CancellationToken ct)
    {
        if (_announced != run.Id)
        {
            Console.WriteLine($"  run     {run.Id}");

            _announced = run.Id;
            _named = false;
        }

        // The model names itself in its first reply, so this cannot go in the start-up header:
        // at start-up all this process knows is a URL. Printing it here means the run on screen
        // says which model produced it, which is the same thing every saved step carries.
        if (!_named && run.ModelId is { Length: > 0 })
        {
            Console.WriteLine($"  model   {run.ModelId}");

            _named = true;
        }

        var outcome = await inner.SaveAsync(run, newSteps, ct);

        foreach (var step in newSteps)
        {
            Console.WriteLine(
                $"  [{step.Ordinal}] {step.Kind} {step.ToolName ?? "-"} → {step.Outcome}" +
                (step.Approver is null ? "" : $" (by {step.Approver})"));
        }

        return outcome;
    }
}
