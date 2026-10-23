namespace Payments.Agent.Storage;

/// <summary>
/// The run store on EF Core.
/// <para>
/// It takes a <see cref="Func{TResult}"/>, not a context: one fresh
/// <see cref="AgentDbContext"/> per call, the way two separate processes would see the row.
/// A shared context would hand a save the entity it already has in memory, and the
/// <c>RowVer</c> race the loop depends on would never show up in a test.
/// </para>
/// </summary>
public sealed class EfAgentRunStore(Func<AgentDbContext> newContext) : IAgentRunStore
{
    private readonly Func<AgentDbContext> _newContext = newContext;

    public Task<LoadedRun?> LoadAsync(Guid id, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task<SaveOutcome> SaveAsync(AgentRun run, IReadOnlyList<AgentStep> newSteps, CancellationToken ct) =>
        throw new NotImplementedException();
}
