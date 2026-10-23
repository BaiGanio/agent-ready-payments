using System.Globalization;

namespace Payments.Agent.Eval;

/// <summary>
/// Prints the table. Two tables, in fact: one row per scenario to read now, and the one totals
/// row that is copied into <c>EVAL-SET.md</c> under "Scores".
/// <para>
/// Markdown, so the totals row is a copy and paste and not a retyping. A number retyped by hand
/// into the file it is compared in is a number that drifts.
/// </para>
/// </summary>
public static class Report
{
    public static void Print(string label, ResolvedAgent agent, IReadOnlyList<Score> scores)
    {
        var passed = scores.Count(s => s.Passed);
        var forbidden = scores.Count(s => s.Forbidden);

        Console.WriteLine("| Scenario | Pass | Steps | Seconds | Tools called | Note |");
        Console.WriteLine("|---|---|---|---|---|---|");

        foreach (var score in scores)
        {
            var tools = score.Outcome.ToolNames.Count == 0
                ? "none"
                : string.Join(", ", score.Outcome.ToolNames);

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"| {score.Scenario.Id} | {(score.Passed ? "yes" : "**no**")} "
                + $"| {score.Outcome.Steps.Count} | {score.Outcome.Seconds:0.0} "
                + $"| {tools} | {score.Note} |"));
        }

        Console.WriteLine();
        Console.WriteLine($"For EVAL-SET.md \"Scores\" (agent {agent.Profile.Name}):");
        Console.WriteLine();
        Console.WriteLine("| Rung | Model | Scenarios passed (of "
            + scores.Count.ToString(CultureInfo.InvariantCulture)
            + ") | Forbidden calls | Mean steps | Mean seconds | Notes |");
        Console.WriteLine("|---|---|---|---|---|---|---|");

        // The mean of an empty set is 0 and not a crash: eval --only S01 is a normal thing to run.
        var meanSteps = scores.Count == 0 ? 0 : scores.Average(s => s.Outcome.Steps.Count);
        var meanSeconds = scores.Count == 0 ? 0 : scores.Average(s => s.Outcome.Seconds);

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"| ? | `{label}` | {passed} | {forbidden} | {meanSteps:0.0} | {meanSeconds:0.0} |  |"));
        Console.WriteLine();

        var failures = scores.Where(s => !s.Passed).ToList();

        if (failures.Count == 0)
        {
            Console.WriteLine("Every scenario passed. Write that line under \"Which scenarios each "
                + "model failed, and how\" too: a clean sheet is a result.");

            return;
        }

        Console.WriteLine("For \"Which scenarios each model failed, and how\":");
        Console.WriteLine();

        foreach (var score in failures)
        {
            Console.WriteLine($"- `{label}` {score.Scenario.Id} "
                + $"(run {score.Outcome.Run.Id}):");

            foreach (var reason in score.Reasons)
            {
                Console.WriteLine($"  - {reason}");
            }
        }
    }
}
