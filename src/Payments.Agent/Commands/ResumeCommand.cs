namespace Payments.Agent.Commands;

/// <summary>
/// Picks a saved run up and carries it on. This is the other half of the crash proof: a run
/// that died in one process finishes in a different one, with nothing shared but the database.
/// <para>
/// It reads the run <b>before</b> it builds anything, for one reason: the agent is not a flag
/// here. A run was started by <c>support-bot</c> or by <c>read-bot</c>, and it has to be
/// resumed by the same one, with the same key, the same ceiling and the same prompt. A
/// <c>--agent</c> on a resume would let a cheap read-only agent finish a refund that a
/// different agent's limits allowed.
/// </para>
/// </summary>
public static class ResumeCommand
{
    public static async Task<int> ExecuteAsync(HostSettings settings, Guid runId)
    {
        var saved = await AgentHost.OpenStore().LoadAsync(runId, CancellationToken.None);

        if (saved is null)
        {
            Console.Error.WriteLine($"There is no run {runId}.");

            return 1;
        }

        if (!AgentProfiles.TryResolve(saved.Run.AgentName, out var agent, out var error))
        {
            // The run names an agent that agents.json no longer has. Guessing another one is
            // exactly the swap this command exists to prevent.
            Console.Error.WriteLine($"Run {runId} was started by '{saved.Run.AgentName}'.");
            Console.Error.WriteLine(error);

            return 2;
        }

        await using var host = await AgentHost.CreateAsync(settings, agent!, crashAfter: null);

        Banner.Print(settings, agent!);

        Console.WriteLine($"resuming {runId}  ({saved.Run.Status}, {saved.Steps.Count} steps)");
        Console.WriteLine();

        // The loop loads the run again, through its own store. One extra read, and worth it:
        // the claim has to be made against the RowVer the loop itself read, or the claim is
        // proving nothing.
        var run = await host.Loop.ResumeAsync(runId, CancellationToken.None);

        Console.WriteLine();

        if (run.Status == RunStatus.Running)
        {
            // The only way the loop hands back a Running run: another worker claimed it first.
            // Nothing was called here, so there is nothing to report but that.
            Console.WriteLine($"Another worker owns run {runId}. Nothing was done.");

            return 1;
        }

        if (run.Status == RunStatus.Failed)
        {
            Console.WriteLine($"FAILED  {run.Reason}");

            // A guard failure has a reason and nothing else, but refund_in_doubt also writes a
            // sentence, and that sentence is the one a person has to act on.
            if (run.Answer is { Length: > 0 } words)
            {
                Console.WriteLine($"{agent!.Profile.Name}> {words}");
            }

            return 1;
        }

        Console.WriteLine($"{agent!.Profile.Name}> {run.Answer}");

        return 0;
    }
}
