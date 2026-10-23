using Microsoft.EntityFrameworkCore;

namespace Payments.Agent.Storage;

/// <summary>
/// EF Core's view of the two tables <c>db/sqlserver/0001_agent_run.sql</c> made.
/// <para>
/// No migrations and no <c>EnsureCreated</c>: the DbUp script is the truth. The mapping
/// arrives in step 9.
/// </para>
/// </summary>
public sealed class AgentDbContext(DbContextOptions<AgentDbContext> options) : DbContext(options)
{
    public DbSet<AgentRun> Runs => Set<AgentRun>();

    public DbSet<AgentStep> Steps => Set<AgentStep>();
}
