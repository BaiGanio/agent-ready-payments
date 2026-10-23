namespace Payments.Agent;

/// <summary>
/// The human, at a terminal. It prints the proposal the tool wrote, word for word, and waits
/// for <c>y</c> or <c>n</c>.
/// <para>
/// It prints the <b>tool's</b> proposal, never the model's summary of it. A model that says
/// "refund 60" while the token holds 600 would get a yes from a person who read the wrong
/// number, and the person would have approved honestly.
/// </para>
/// <para>
/// There is no default and no timeout: anything that is not a clear yes is a no. A prompt that
/// takes silence for a yes is not an approval, and a refund is not worth guessing about.
/// </para>
/// </summary>
public sealed class ConsoleApprovalChannel : IApprovalChannel
{
    public Task<Approval> AskAsync(AgentRun run, PendingRefund refund, CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("  ── a refund needs your approval ──────────────────────────");
        Console.WriteLine($"  {refund.Proposal}");
        Console.WriteLine($"  payment  {refund.PaymentId}");
        Console.WriteLine($"  amount   {refund.Amount} {refund.Currency}");
        Console.WriteLine($"  agent    {run.AgentName} ({run.ModelId})");
        Console.WriteLine("  ──────────────────────────────────────────────────────────");
        Console.Write("  Refund this? [y/N] ");

        var answer = Console.ReadLine();

        Console.WriteLine();

        var yes = string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase);

        // Who said it, not just what was said. The approval step keeps this name, so months
        // later a reviewer can read "proposed by agent X, approved by person Z".
        return Task.FromResult(new Approval(yes, Environment.UserName));
    }
}
