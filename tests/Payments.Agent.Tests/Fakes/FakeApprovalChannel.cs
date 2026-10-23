using Payments.Agent.Storage;

namespace Payments.Agent.Tests;

/// <summary>
/// One ask the channel saw. <paramref name="SavedStatusWhenAsked"/> is the point: the run must
/// already be <b>saved</b> as <c>WaitingForHuman</c> at the moment the human is asked. If it is
/// only set on the object in memory, a crash during the wait loses the pending refund and the
/// human's yes lands on nothing.
/// </summary>
public sealed record RecordedAsk(
    Guid RunId,
    PendingRefund Refund,
    RunStatus? SavedStatusWhenAsked);

/// <summary>
/// The human, as a fake. It answers at once and always the same way, so an approval test is
/// about the loop's gate and not about timing.
/// </summary>
public sealed class FakeApprovalChannel(IAgentRunStore store) : IApprovalChannel
{
    private Approval _answer = new(Approved: true, By: "lk");

    public List<RecordedAsk> Asks { get; } = [];

    public FakeApprovalChannel Approve(string by = "lk")
    {
        _answer = new Approval(Approved: true, by);

        return this;
    }

    public FakeApprovalChannel Deny(string by = "lk")
    {
        _answer = new Approval(Approved: false, by);

        return this;
    }

    public async Task<Approval> AskAsync(AgentRun run, PendingRefund refund, CancellationToken ct)
    {
        // Read the store, not the run object the loop handed us. The question is what another
        // process would see while the human thinks.
        var saved = await store.LoadAsync(run.Id, ct);

        Asks.Add(new RecordedAsk(run.Id, refund, saved?.Run.Status));

        return _answer;
    }
}
