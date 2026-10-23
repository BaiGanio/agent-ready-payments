using System.Diagnostics;
using System.Globalization;
using Payments.Agent.Commands;
using Payments.Agent.Storage;

namespace Payments.Agent.Eval;

/// <summary>
/// Runs the whole evaluation set against the dev stack and prints the score table.
/// <para>
/// It is a console command and <b>not</b> a test in <c>dotnet test</c>. It needs a live model, it
/// takes minutes, and a model answers differently between runs. A suite that goes red when the
/// weather changes stops being trusted, and then the suite that catches real bugs is ignored too.
/// </para>
/// <para>
/// One fresh <see cref="AgentLoop"/> per scenario, with that scenario's own payments and its own
/// human seat. Nothing is shared between scenarios except the model and the MCP server, which is
/// also the only thing a model comparison is allowed to hold still.
/// </para>
/// </summary>
public static class EvalRunner
{
    public static async Task<int> ExecuteAsync(
        HostSettings settings, ResolvedAgent agent, string label, string? only)
    {
        var devKey = Environment.GetEnvironmentVariable("PAYMENTS_DEV_KEY");

        if (string.IsNullOrWhiteSpace(devKey))
        {
            // The scenarios create and capture payments before the agent runs. No agent key may
            // do that, so the bench needs the service key and says so plainly.
            Console.Error.WriteLine(
                "PAYMENTS_DEV_KEY is not set. eval arranges payments, which no agent key may do.");
            Console.Error.WriteLine("Fill it in .env, then: set -a; source .env; set +a");

            return 2;
        }

        IReadOnlyList<Scenario> wanted = only is null
            ? Scenarios.All
            : [.. Scenarios.All.Where(s =>
                string.Equals(s.Id, only, StringComparison.OrdinalIgnoreCase))];

        if (wanted.Count == 0)
        {
            Console.Error.WriteLine($"There is no scenario called '{only}'. "
                + "The set has: " + string.Join(", ", Scenarios.All.Select(s => s.Id)) + ".");

            return 2;
        }

        using var api = new HttpClient { BaseAddress = new Uri(settings.ApiUrl) };

        api.DefaultRequestHeaders.Add("X-Api-Key", devKey);

        // One MCP child and one model client for the whole set. A fresh child per scenario would
        // add a process start to every row's seconds and measure dotnet, not the model.
        await using var host = await AgentHost.CreateAsync(settings, agent, crashAfter: null);

        Console.WriteLine($"eval    label {label}, {wanted.Count} scenario(s)");
        Banner.Print(settings, agent);

        var store = AgentHost.OpenStore();
        var scores = new List<Score>();

        foreach (var scenario in wanted)
        {
            Console.WriteLine($"{scenario.Id}  arranging …");

            var score = await ScoreAsync(host, store, api, scenario);

            scores.Add(score);

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{scenario.Id}  {(score.Passed ? "PASS" : "FAIL")}  "
                + $"{score.Outcome.Steps.Count} steps, {score.Outcome.Seconds:0.0}s, {score.Note}"));
            Console.WriteLine($"      run {score.Outcome.Run.Id}");
            Console.WriteLine();
        }

        Report.Print(label, agent, scores);

        // 0 only when every scenario passed, so a script can gate on the whole set.
        return scores.All(s => s.Passed) ? 0 : 1;
    }

    /// <summary>
    /// Arrange, run, read back, score. The arrange is outside the stopwatch: the seconds column
    /// compares models, and creating five payments is the same work whichever model is loaded.
    /// </summary>
    private static async Task<Score> ScoreAsync(
        AgentHost host, IAgentRunStore store, HttpClient api, Scenario scenario)
    {
        var arrange = new Arrange(api);

        await scenario.ArrangeAsync(arrange);

        var channel = new EvalChannel(scenario.Approval);
        var recorder = new RecordingStore(store);
        var loop = host.NewLoop(channel, recorder);

        var clock = Stopwatch.StartNew();

        // No token of our own. The loop has its own turn deadline, and a second deadline here
        // would cut a run off halfway and record the bench's impatience as the model's failure.
        var run = await loop.StartAsync(arrange.Fill(scenario.Request), CancellationToken.None);

        clock.Stop();

        var outcome = new Outcome(
            run,
            recorder.Steps,
            channel.Asked,
            await arrange.ReadAllAsync(),
            clock.Elapsed.TotalSeconds);

        var approvalOk = scenario.Approval == ApprovalAnswer.NeverAsked
            ? !channel.Asked
            : channel.Asked;

        // The forbidden check runs first in spirit but is computed like the rest: a scenario is
        // scored on every axis, so a failed row says which axis, not only that it failed.
        return new Score(
            scenario,
            outcome,
            ToolsOk: InOrder(outcome.ToolNames, scenario.ExpectedTools),
            StateOk: Safely(scenario.FinalStateOk, outcome, whenItThrows: false),
            // A forbidden check that throws counts as forbidden. The safe side of "did something
            // terrible happen?" is yes, because a scenario that passes on a broken check is a
            // model that looks clean for the wrong reason.
            Forbidden: Safely(scenario.ForbiddenHappened, outcome, whenItThrows: true),
            StepsOk: outcome.Steps.Count <= scenario.MaxSteps,
            ApprovalOk: approvalOk);
    }

    /// <summary>
    /// True when every expected tool appears, in that order, somewhere in what was really
    /// called. Extra reads in between are allowed; the forbidden list catches the extras that
    /// matter. A weak model often reads a payment twice before it acts, and failing it for that
    /// would hide the models that refund the wrong payment.
    /// </summary>
    private static bool InOrder(IReadOnlyList<string> called, IReadOnlyList<string> expected)
    {
        var at = 0;

        foreach (var name in called)
        {
            if (at < expected.Count && name == expected[at])
            {
                at++;
            }
        }

        return at == expected.Count;
    }

    /// <summary>
    /// A check that throws is a failed check, never a crashed run.
    /// <para>
    /// <see cref="Outcome.Payment"/> throws on a handle no scenario arranged, which is a typo in
    /// the set. Letting it out would lose the nine scenarios that already ran, so it is caught,
    /// printed, and counted as a miss.
    /// </para>
    /// </summary>
    private static bool Safely(Func<Outcome, bool> check, Outcome outcome, bool whenItThrows)
    {
        try
        {
            return check(outcome);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"      a check threw {error.GetType().Name}: {error.Message}");

            return whenItThrows;
        }
    }
}
