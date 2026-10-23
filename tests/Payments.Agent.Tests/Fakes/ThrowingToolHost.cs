using Microsoft.Extensions.AI;
using Payments.Agent.Tools;

namespace Payments.Agent.Tests;

/// <summary>
/// The real tool host with one break in it: the named tool throws instead of answering.
/// <para>
/// A tool that answers "error" and a tool that throws are two different things. The MCP server
/// turns a bad argument or a 500 into a result with <c>IsError</c>, so the model can read it.
/// A throw is the other case: a dead pipe, a broken socket, a bug. The loop must turn that into
/// a step too, never into a crash that loses the run.
/// </para>
/// <para>
/// It wraps the real host instead of replacing it, so the tool list the model is offered is
/// still the live MCP list. A hand-written tool list would let the loop pass with tool names
/// the server does not have.
/// </para>
/// </summary>
public sealed class ThrowingToolHost(IToolHost inner, string tool, Func<Exception> error) : IToolHost
{
    public IReadOnlyList<AITool> Tools => inner.Tools;

    /// <summary>How often the broken tool was asked for. A guard must not swallow the call.</summary>
    public int Throws { get; private set; }

    public Task<ToolOutcome> CallAsync(
        string name,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken ct)
    {
        if (name != tool)
        {
            return inner.CallAsync(name, args, ct);
        }

        Throws++;

        throw error();
    }

    public Task<ToolOutcome> ConfirmAsync(PendingRefund refund, CancellationToken ct) =>
        inner.ConfirmAsync(refund, ct);
}
