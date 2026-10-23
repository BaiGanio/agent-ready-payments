using Microsoft.EntityFrameworkCore;

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

    public async Task<LoadedRun?> LoadAsync(Guid id, CancellationToken ct)
    {
        await using var ctx = _newContext();

        // AsNoTracking on purpose. The caller gets a plain object carrying the RowVer bytes it
        // read, and nothing in EF remembers the row. That is what makes two loads behave like
        // two workers instead of two views of one tracked entity.
        var run = await ctx.Runs
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == id, ct);

        if (run is null) return null;

        var steps = await ctx.Steps
            .AsNoTracking()
            .Where(s => s.RunId == id)
            .OrderBy(s => s.Ordinal)
            .ToListAsync(ct);

        return new LoadedRun(run, steps);
    }

    public async Task<SaveOutcome> SaveAsync(
        AgentRun run,
        IReadOnlyList<AgentStep> newSteps,
        CancellationToken ct)
    {
        await using var ctx = _newContext();

        if (run.RowVer.Length == 0)
        {
            // A run the database has never seen: no version yet. [Timestamp] keeps RowVer out
            // of the INSERT and reads the new value back afterwards.
            ctx.Runs.Add(run);
        }
        else
        {
            var entry = ctx.Attach(run);

            entry.State = EntityState.Modified;

            // This line is the whole race. A fresh context has never seen the row, so it does
            // not know which version we read. We tell it, and EF adds
            // WHERE Id = @id AND RowVer = @original. A worker that lost writes zero rows.
            entry.Property(r => r.RowVer).OriginalValue = run.RowVer;
        }

        ctx.Steps.AddRange(newSteps);

        try
        {
            // One SaveChanges is one transaction, so the run and its new steps land together
            // or not at all. A step without the run that points at it would make a resume read
            // a history that never happened.
            await ctx.SaveChangesAsync(ct);

            return SaveOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Only this exception. A DbUpdateException is a real bug — a broken CHECK, a
            // missing column — and swallowing it would hide it.
            return SaveOutcome.Conflict;
        }
    }
}
