using Microsoft.Extensions.AI;
using Payments.Agent.Storage;
using Payments.Agent.Tools;

namespace Payments.Agent;

/// <summary>
/// The agent loop: ask the model, run the tool it asked for, feed the result back, save, repeat.
/// <para>
/// Everything it touches is an interface, so the same loop runs against <c>llama-server</c> and
/// against a scripted fake, and the tests never need a real model.
/// </para>
/// </summary>
public sealed class AgentLoop(
    IChatClient model,
    IToolHost tools,
    IApprovalChannel approvals,
    IAgentRunStore store,
    AgentOptions options,
    TimeProvider clock)
{
    /// <summary>Starts a new run from one human request and carries it as far as it goes.</summary>
    public Task<AgentRun> StartAsync(string request, CancellationToken ct) =>
        throw new NotImplementedException();

    /// <summary>Picks up a saved run: after a crash, or after a human answered.</summary>
    public Task<AgentRun> ResumeAsync(Guid runId, CancellationToken ct) =>
        throw new NotImplementedException();
}
