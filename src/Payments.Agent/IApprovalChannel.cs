namespace Payments.Agent;

/// <summary>A human's answer to one proposed refund, and who gave it.</summary>
public sealed record Approval(bool Approved, string By);

/// <summary>
/// How the loop asks a human. It is an interface because the console asks at a prompt, a real
/// platform asks through a queue and a ticket, and a test answers instantly. The loop must not
/// know which.
/// </summary>
public interface IApprovalChannel
{
    Task<Approval> AskAsync(AgentRun run, PendingRefund refund, CancellationToken ct);
}
