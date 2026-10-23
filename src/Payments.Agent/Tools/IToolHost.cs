using Microsoft.Extensions.AI;

namespace Payments.Agent.Tools;

/// <summary>
/// What one tool call came back with.
/// </summary>
/// <param name="IsError">The tool said no. The loop feeds this back to the model as a result,
/// not as a crash: a model that is told "that id is not a UUID" can fix its own call.</param>
/// <param name="Json">The tool's single text block, as it stands. It goes into the history.</param>
/// <param name="Proposal">Set when the tool asked for a human. Null otherwise.</param>
public sealed record ToolOutcome(bool IsError, string Json, PendingRefund? Proposal);

/// <summary>
/// The loop's view of the tools. It is this small so the loop never learns whether the tools
/// are MCP over a pipe, MCP over HTTP, or a fake in a test.
/// </summary>
public interface IToolHost
{
    /// <summary>What the model is offered. Read once at start-up, not per call.</summary>
    IReadOnlyList<AITool> Tools { get; }

    Task<ToolOutcome> CallAsync(
        string name,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken ct);

    /// <summary>Phase two of a refund: the same call again, with the token a human approved.</summary>
    Task<ToolOutcome> ConfirmAsync(PendingRefund refund, CancellationToken ct);
}
