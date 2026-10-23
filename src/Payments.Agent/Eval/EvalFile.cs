using System.Globalization;
using System.Text;
using System.Text.Json;
using Payments.Agent.Commands;

namespace Payments.Agent.Eval;

/// <summary>
/// Writes one run of the evaluation set to its own file.
/// <para>
/// The console table scrolls away, and the next run overwrites nothing but your memory. A score
/// you cannot open again is a score you cannot defend: the first question about "rung 1 got 5 out
/// of 10" is always <b>which five, and how did the other five go wrong</b>, and that answer is
/// gone the moment the terminal is closed.
/// </para>
/// <para>
/// One file per run, named by the time it started, so two runs of the same model never collide
/// and the folder sorts into a history. Markdown, because the totals row is copied into
/// <c>EVAL-SET.md</c> by hand and a number retyped is a number that drifts.
/// </para>
/// <para>
/// The file holds more than the console does: the request as it was really typed, the answer the
/// model gave, and every saved step with its code. Those are what the "how did it fail" lines in
/// <c>EVAL-SET.md</c> are written from.
/// </para>
/// </summary>
public static class EvalFile
{
    /// <summary>Under the lab, next to the evaluation set the scores belong to.</summary>
    public const string Folder = "labs/09-agent-loop/eval-runs";

    /// <summary>
    /// Writes the report and returns the path. <paramref name="outPath"/> overrides the default
    /// name and folder, for a run you want to keep somewhere else.
    /// </summary>
    public static async Task<string> WriteAsync(
        HostSettings settings,
        ResolvedAgent agent,
        string label,
        DateTimeOffset startedUtc,
        IReadOnlyList<Score> scores,
        string? outPath)
    {
        var path = outPath ?? Path.Combine(
            settings.RepoRoot,
            Folder,
            $"{startedUtc:yyyyMMdd-HHmmss}Z-{Slug(label)}.md");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await File.WriteAllTextAsync(path, Build(settings, agent, label, startedUtc, scores));

        return path;
    }

    /// <summary>
    /// A file name with nothing in it that a shell or a filesystem argues about. A model id such
    /// as <c>unsloth/gemma-4-E4B-it-qat-GGUF:UD-Q4_K_XL</c> carries a slash, and a slash in a
    /// file name is a folder nobody asked for.
    /// </summary>
    private static string Slug(string label)
    {
        var clean = new StringBuilder(label.Length);

        foreach (var letter in label)
        {
            clean.Append(char.IsLetterOrDigit(letter) || letter is '-' or '.' ? letter : '-');
        }

        return clean.ToString().Trim('-');
    }

    private static string Build(
        HostSettings settings,
        ResolvedAgent agent,
        string label,
        DateTimeOffset startedUtc,
        IReadOnlyList<Score> scores)
    {
        var text = new StringBuilder();

        var passed = scores.Count(s => s.Passed);
        var forbidden = scores.Count(s => s.Forbidden);

        // The model names itself in its first reply, so it is read off a run and not off the
        // command line: --label says which rung you meant, this says which weights answered.
        var modelId = scores
            .Select(s => s.Outcome.Run.ModelId)
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)) ?? "unknown";

        text.AppendLine($"# Evaluation run — {label}");
        text.AppendLine();
        text.AppendLine("Written by the runner. Do not edit it: it is the evidence for one run.");
        text.AppendLine($"Copy its totals row into [`../EVAL-SET.md`](../EVAL-SET.md) yourself.");
        text.AppendLine();
        text.AppendLine("| | |");
        text.AppendLine("|---|---|");
        text.AppendLine($"| Label | `{label}` |");
        text.AppendLine($"| Model id it answered with | `{modelId}` |");
        text.AppendLine($"| Agent | `{agent.Profile.Name}` ({agent.Profile.PromptVersion}) |");
        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"| Refund ceiling | {agent.Profile.RefundCeiling:0.00} |"));
        text.AppendLine($"| Model endpoint | `{settings.LlmUrl}` |");
        text.AppendLine($"| API | `{settings.ApiUrl}` |");
        text.AppendLine($"| Started (UTC) | {startedUtc:yyyy-MM-dd HH:mm:ss} |");
        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"| Passed | **{passed} of {scores.Count}** |"));
        text.AppendLine();

        text.AppendLine("## Scenarios");
        text.AppendLine();
        text.AppendLine("| Scenario | Pass | Steps | Seconds | Tools called | Note |");
        text.AppendLine("|---|---|---|---|---|---|");

        foreach (var score in scores)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| [{score.Scenario.Id}](#{score.Scenario.Id.ToLowerInvariant()}) "
                + $"| {(score.Passed ? "yes" : "**no**")} "
                + $"| {score.Outcome.Steps.Count} | {score.Outcome.Seconds:0.0} "
                + $"| {Tools(score.Outcome.ToolNames)} | {score.Note} |"));
        }

        var meanSteps = scores.Count == 0 ? 0 : scores.Average(s => s.Outcome.Steps.Count);
        var meanSeconds = scores.Count == 0 ? 0 : scores.Average(s => s.Outcome.Seconds);

        text.AppendLine();
        text.AppendLine("## The row for EVAL-SET.md");
        text.AppendLine();
        text.AppendLine($"| Rung | Model | Scenarios passed (of {scores.Count}) "
            + "| Forbidden calls | Mean steps | Mean seconds | Notes |");
        text.AppendLine("|---|---|---|---|---|---|---|");
        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"| ? | `{label}` | {passed} | {forbidden} | {meanSteps:0.0} | {meanSeconds:0.0} |  |"));
        text.AppendLine();
        text.AppendLine("The rung and the note are yours. The runner does not know which rung of "
            + "the ladder you loaded, and it must not guess.");
        text.AppendLine();

        var failures = scores.Where(s => !s.Passed).ToList();

        text.AppendLine("## Which scenarios failed, and how");
        text.AppendLine();

        if (failures.Count == 0)
        {
            text.AppendLine("None. Every scenario passed.");
        }
        else
        {
            foreach (var score in failures)
            {
                text.AppendLine($"- **{score.Scenario.Id}** (run `{score.Outcome.Run.Id}`)");

                foreach (var reason in score.Reasons)
                {
                    text.AppendLine($"  - {reason}");
                }
            }
        }

        text.AppendLine();
        text.AppendLine("## Every scenario, step by step");
        text.AppendLine();
        text.AppendLine("This is what the console does not keep. The failure lines above are "
            + "written from here.");

        foreach (var score in scores)
        {
            Detail(text, score);
        }

        return text.ToString();
    }

    private static void Detail(StringBuilder text, Score score)
    {
        var outcome = score.Outcome;
        var run = outcome.Run;

        text.AppendLine();
        text.AppendLine($"### {score.Scenario.Id}");
        text.AppendLine();
        text.AppendLine($"- **Result**: {(score.Passed ? "pass" : "**fail**")}");
        text.AppendLine($"- **Run id**: `{run.Id}` — `dotnet run --project src/Payments.Agent "
            + $"-- show {run.Id}`");
        text.AppendLine($"- **Request**: `{score.Scenario.Request}`");
        text.AppendLine($"- **Approval expected**: {score.Scenario.Approval}, "
            + $"and a human was {(outcome.Asked ? "asked" : "never asked")}");
        text.AppendLine($"- **Tools expected**: {Tools(score.Scenario.ExpectedTools)}");
        text.AppendLine($"- **Tools called**: {Tools(outcome.ToolNames)}");
        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- **Run status**: {run.Status}{(run.Reason is null ? "" : $" — `{run.Reason}`")}, "
            + $"{outcome.Steps.Count} step(s) of at most {score.Scenario.MaxSteps}, "
            + $"{outcome.Seconds:0.0}s"));

        // The five axes, each named, then every failed check as its own sentence. "fail" on its
        // own sends you back to the source to work out which check it was, and by then you are
        // guessing at a run that will never happen the same way twice.
        text.AppendLine($"- **Checks**: tools {Mark(score.ToolsOk)}, "
            + $"final state {Mark(score.StateOk)}, "
            + $"steps {Mark(score.StepsOk)}, "
            + $"approval {Mark(score.ApprovalOk)}, "
            + $"forbidden {(score.Forbidden ? "**happened**" : "no")}");

        if (score.Reasons.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("What went wrong:");
            text.AppendLine();

            foreach (var reason in score.Reasons)
            {
                text.AppendLine($"- {reason}");
            }
        }

        if (outcome.Payments.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("| Payment | Id | Status | Captured | Refunded | Refundable left |");
            text.AppendLine("|---|---|---|---|---|---|");

            foreach (var (handle, facts) in outcome.Payments)
            {
                text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"| `{{{handle}}}` | `{facts.Id}` | {facts.Status} "
                    + $"| {facts.CapturedAmount:0.00} | {facts.RefundedAmount:0.00} "
                    + $"| {facts.RefundableRemaining:0.00} |"));
            }
        }

        text.AppendLine();
        text.AppendLine("The answer, as the run saved it:");
        text.AppendLine();
        text.AppendLine("> " + (string.IsNullOrWhiteSpace(run.Answer)
            ? "_(no answer: the run did not finish)_"
            : run.Answer.ReplaceLineEndings(" ")));

        if (outcome.Steps.Count == 0)
        {
            return;
        }

        text.AppendLine();
        text.AppendLine("| # | Kind | Tool | Outcome | Code | Approver |");
        text.AppendLine("|---|---|---|---|---|---|");

        foreach (var step in outcome.Steps)
        {
            text.AppendLine($"| {step.Ordinal} | {step.Kind} | {step.ToolName ?? "—"} "
                + $"| {step.Outcome} | {CodeIn(step.OutputJson) ?? "—"} "
                + $"| {step.Approver ?? "—"} |");
        }
    }

    private static string Mark(bool ok) => ok ? "ok" : "**no**";

    private static string Tools(IReadOnlyList<string> names) =>
        names.Count == 0 ? "none" : string.Join(", ", names.Select(n => $"`{n}`"));

    /// <summary>
    /// The <c>error</c> field of a tool refusal, which is the one part of a tool failure that is
    /// a contract. The English next to it is rewritten whenever a sentence reads badly.
    /// </summary>
    private static string? CodeIn(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var root = JsonDocument.Parse(json).RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (root.TryGetProperty("error", out var error))
            {
                return $"`{error.GetString()}`";
            }

            // A successful read answers with result: found, not_found, confirmation_required, ok.
            // That word is the fact a scenario checks, so it belongs in the same column.
            return root.TryGetProperty("result", out var result)
                ? $"`{result.GetString()}`"
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
