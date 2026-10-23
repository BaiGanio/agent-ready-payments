using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace Payments.Agent.Storage;

/// <summary>
/// EF Core's view of the two tables <c>db/sqlserver/0001_agent_run.sql</c> made.
/// <para>
/// No migrations and no <c>EnsureCreated</c>: the DbUp script is the truth. EF Core owns the
/// shape of these two tables, but it does not create them.
/// </para>
/// </summary>
public sealed class AgentDbContext(DbContextOptions<AgentDbContext> options) : DbContext(options)
{
    public DbSet<AgentRun> Runs => Set<AgentRun>();

    public DbSet<AgentStep> Steps => Set<AgentStep>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var run = modelBuilder.Entity<AgentRun>();

        run.ToTable("AgentRun");

        // The column is varchar(20) with a CHECK, so the enum goes down as its name. As an
        // int it would be refused by the check, and a saved 0 would tell a human nothing.
        run.Property(r => r.Status).HasConversion<string>();

        // AIJsonUtilities.DefaultOptions is the point of these two lines. Those options know
        // every AIContent subtype. Plain JsonSerializer options write a FunctionCallContent as
        // an empty {}, and a resumed run would hand the model a history with the tool calls
        // rubbed out.
        run.Property(r => r.Messages).HasConversion(
            v => JsonSerializer.Serialize(v, AIJsonUtilities.DefaultOptions),
            s => JsonSerializer.Deserialize<List<ChatMessage>>(s, AIJsonUtilities.DefaultOptions)!);

        // No null handling in the lambdas: EF Core never calls a converter for a null value,
        // it writes NULL. So clearing PendingCall clears the column.
        run.Property(r => r.PendingCall).HasConversion(
            v => JsonSerializer.Serialize(v, AIJsonUtilities.DefaultOptions),
            s => JsonSerializer.Deserialize<PendingRefund>(s, AIJsonUtilities.DefaultOptions));

        modelBuilder.Entity<AgentStep>().ToTable("AgentStep");
    }
}
