namespace Payments.Agent.Eval;

/// <summary>
/// The human seat, played from the scenario. It answers at once and remembers that it was asked.
/// <para>
/// Being asked is itself a measurement. Seven of the ten scenarios say no human may be asked,
/// because the guard that should have stopped the run sits before the question. A channel that
/// only returned an answer could not tell a model that was stopped by a guard from one that was
/// stopped by a person saying no, and those are two different models.
/// </para>
/// <para>
/// On a <see cref="ApprovalAnswer.NeverAsked"/> scenario it answers <b>no</b>, so a model that
/// got this far still moves no money while the scenario is marked failed.
/// </para>
/// </summary>
public sealed class EvalChannel(ApprovalAnswer answer) : IApprovalChannel
{
    /// <summary>The name on the approval step, so a saved run says the bench approved it.</summary>
    public const string Approver = "eval";

    public bool Asked { get; private set; }

    public Task<Approval> AskAsync(AgentRun run, PendingRefund refund, CancellationToken ct)
    {
        Asked = true;

        return Task.FromResult(new Approval(answer == ApprovalAnswer.Yes, Approver));
    }
}
