namespace Payments.Agent.Commands;

/// <summary>
/// A prompt line, and one run per line. This is the demo's seat.
/// <para>
/// Every line is a <b>new run</b>, not a longer conversation: the run is the unit that is
/// saved, resumed and shown, so one line in equals one row to point at. Keeping one chat
/// history across lines would make the evidence for "who asked for this refund" a transcript
/// instead of a request.
/// </para>
/// <para>
/// The MCP child and the model client are started once and reused, because starting a child
/// process per line would add a second to every question in front of an audience.
/// </para>
/// </summary>
public static class ChatCommand
{
    public static async Task<int> ExecuteAsync(HostSettings settings, ResolvedAgent agent)
    {
        await using var host = await AgentHost.CreateAsync(settings, agent, crashAfter: null);

        Banner.Print(settings, agent);
        Console.WriteLine("An empty line or Ctrl-D ends the chat.");
        Console.WriteLine();

        while (true)
        {
            Console.Write("you> ");

            var line = Console.ReadLine();

            // null is Ctrl-D (end of input), empty is a bare Enter. Both end the chat.
            if (string.IsNullOrWhiteSpace(line))
            {
                Console.WriteLine();
                Console.WriteLine("bye.");

                return 0;
            }

            Console.WriteLine();

            var run = await host.Loop.StartAsync(line, CancellationToken.None);

            Console.WriteLine();

            // A failed run is said out loud as a failure. An agent that answers in its own
            // words after a guard stopped it is the most dangerous bug in this lab.
            Console.WriteLine(run.Status == RunStatus.Failed
                ? $"{agent.Profile.Name}> I stopped without an answer: {run.Reason}"
                : $"{agent.Profile.Name}> {run.Answer}");

            Console.WriteLine();
        }
    }
}
