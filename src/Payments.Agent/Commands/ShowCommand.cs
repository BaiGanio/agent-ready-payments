using System.Text.RegularExpressions;

namespace Payments.Agent.Commands;

/// <summary>
/// Prints one saved run and every step in it: the demo's evidence.
/// <para>
/// It reads the store and nothing else. No model, no MCP child, no API: the whole point is
/// that the answer to "who moved this money, and who said yes?" is a database row, available
/// months later, with the model long gone.
/// </para>
/// <para>
/// It <b>never</b> prints a confirmation token. A token on a screen or in a scrollback is a
/// token a person can send, and the one rule of this lab is that only the host confirms, after
/// a human said yes. The saved tool output really contains one, so it is hidden here, not
/// trusted to be absent.
/// </para>
/// </summary>
public static partial class ShowCommand
{
    /// <summary>Matches the token wherever it sits in saved JSON, in either quoting style.</summary>
    [GeneratedRegex("\"confirmation_token\"\\s*:\\s*\"[^\"]*\"")]
    private static partial Regex Token();

    public static async Task<int> ExecuteAsync(Guid id)
    {
        var loaded = await AgentHost.OpenStore().LoadAsync(id, CancellationToken.None);

        if (loaded is null)
        {
            Console.Error.WriteLine($"There is no run {id}.");

            return 1;
        }

        var (run, steps) = (loaded.Run, loaded.Steps);

        Console.WriteLine($"run      {run.Id}");
        Console.WriteLine($"agent    {run.AgentName}");
        Console.WriteLine($"model    {run.ModelId ?? "-"}");
        Console.WriteLine($"prompt   {run.PromptVersion}");
        Console.WriteLine($"status   {run.Status}{(run.Reason is null ? "" : $"  ({run.Reason})")}");
        Console.WriteLine($"created  {run.CreatedUtc:u}");
        Console.WriteLine($"updated  {run.UpdatedUtc:u}");
        Console.WriteLine($"steps    {steps.Count}");

        if (run.PendingCall is { } pending)
        {
            // The amount and the payment, never the token.
            Console.WriteLine(
                $"waiting  {pending.Amount} {pending.Currency} on {pending.PaymentId}");
        }

        Console.WriteLine();

        foreach (var step in steps)
        {
            Console.WriteLine($"  [{step.Ordinal}] {step.Kind} {step.ToolName ?? "-"} → {step.Outcome}"
                + (step.Approver is null ? "" : $" (by {step.Approver})")
                + $"  {step.DurationMs} ms");

            if (step.InputJson is { Length: > 0 } input)
            {
                Console.WriteLine($"       in   {Safe(input)}");
            }

            if (step.OutputJson is { Length: > 0 } output)
            {
                Console.WriteLine($"       out  {Safe(output)}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"answer   {run.Answer ?? "-"}");

        return 0;
    }

    /// <summary>Hide the token, then keep it to one line so a long list stays readable.</summary>
    private static string Safe(string json)
    {
        var hidden = Token().Replace(json, "\"confirmation_token\":\"<hidden>\"");

        var line = hidden.ReplaceLineEndings(" ");

        return line.Length <= 240 ? line : line[..240] + " …";
    }
}
