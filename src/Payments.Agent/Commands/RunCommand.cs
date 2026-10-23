using System.Globalization;

namespace Payments.Agent.Commands;

/// <summary>
/// One request, one run, one process. This is the shape a script or a cron job uses, and the
/// shape the crash-and-resume proof needs.
/// </summary>
public static class RunCommand
{
    public static async Task<int> ExecuteAsync(
        HostSettings settings, ResolvedAgent agent, string request, int? crashAfter)
    {
        await using var host = await AgentHost.CreateAsync(settings, agent, crashAfter);

        Banner.Print(settings, agent);

        var run = await host.Loop.StartAsync(request, CancellationToken.None);

        Console.WriteLine();
        Console.WriteLine(run.Status == RunStatus.Failed
            ? $"FAILED  {run.Reason}"
            : $"{agent.Profile.Name}> {run.Answer}");

        // The exit code is for a script, not for a person: 0 finished, 1 a guard stopped it,
        // 2 a set-up mistake, 99 the deliberate crash.
        return run.Status == RunStatus.Failed ? 1 : 0;
    }
}

/// <summary>
/// The three lines that say which agent, which model endpoint and which API this process is
/// talking to. The model's own id is not here: at start-up all this process knows is a URL.
/// <see cref="Storage.PrintingStore"/> prints the id when the first reply names it.
/// </summary>
public static class Banner
{
    public static void Print(HostSettings settings, ResolvedAgent agent)
    {
        // InvariantCulture on the ceiling: this machine's locale writes 500,00, and a comma is
        // the one money shape the tools refuse, so it must never appear next to an amount.
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"agent   {agent.Profile.Name} ({agent.Profile.PromptVersion}), "
            + $"key from {agent.Profile.ApiKeyEnv}, "
            + $"refund ceiling {agent.Profile.RefundCeiling:0.00}"));
        Console.WriteLine($"llm     {settings.LlmUrl}");
        Console.WriteLine($"api     {settings.ApiUrl}");
        Console.WriteLine();
    }
}
