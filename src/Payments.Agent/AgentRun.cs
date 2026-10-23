using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.AI;

namespace Payments.Agent;

/// <summary>Where a run stands. The same four words the <c>CK_AgentRun_Status</c> check allows.</summary>
public enum RunStatus { Running, WaitingForHuman, Done, Failed }

/// <summary>
/// A refund the agent proposed and nobody has approved yet. It is saved on the run, so the
/// human can answer minutes later, in another process.
/// </summary>
public sealed record PendingRefund(
    string PaymentId,
    string Amount,
    string Currency,
    string Token,
    string Proposal);

/// <summary>
/// One agent run: the whole conversation, plus where it stands. This is the waiter's notepad.
/// Lose it and the run is gone, so every pass of the loop saves it.
/// </summary>
public sealed class AgentRun
{
    public Guid Id { get; set; }

    /// <summary>Which configured agent ran: <c>support-bot</c>, <c>read-bot</c>.</summary>
    public string AgentName { get; set; } = "";

    /// <summary>Null until the first model reply comes back and tells us its id.</summary>
    public string? ModelId { get; set; }

    public string PromptVersion { get; set; } = "";

    public RunStatus Status { get; set; } = RunStatus.Running;

    /// <summary>Tool-calling passes so far. The step cap reads this.</summary>
    public int StepCount { get; set; }

    /// <summary>
    /// The whole history, saved as JSON. A tool call and its result are messages too, so a
    /// resumed run can hand the model exactly what it saw before.
    /// </summary>
    public List<ChatMessage> Messages { get; set; } = [];

    /// <summary>The refund a human has not answered yet. Null unless <see cref="RunStatus.WaitingForHuman"/>.</summary>
    public PendingRefund? PendingCall { get; set; }

    /// <summary>The final answer, when <see cref="RunStatus.Done"/>.</summary>
    public string? Answer { get; set; }

    /// <summary>Why it failed: <c>tool_failure_budget</c>, <c>step_cap</c>, ...</summary>
    public string? Reason { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    /// <summary>
    /// The database's own counter. <c>[Timestamp]</c> makes EF Core add
    /// <c>WHERE RowVer = @original</c> to every update, so a save that lost a race writes
    /// zero rows instead of flattening the winner.
    /// </summary>
    [Timestamp]
    public byte[] RowVer { get; set; } = [];
}

/// <summary>
/// One thing that happened inside a run: a tool call, an approval, or a guard that fired.
/// <para>
/// There is <b>no</b> navigation property back to the run, and the run holds no list of steps.
/// A new step gets its <c>Id</c> in C#, and EF Core's <c>Attach</c> treats any entity that has
/// a key as already saved. A new step hanging off an attached run would quietly never be
/// inserted. So the store takes the new steps as their own list.
/// </para>
/// </summary>
public sealed class AgentStep
{
    public Guid Id { get; set; }

    public Guid RunId { get; set; }

    /// <summary>1, 2, 3 ... within one run. Unique per run in the database.</summary>
    public int Ordinal { get; set; }

    /// <summary><c>tool</c>, <c>approval</c> or <c>guard</c>.</summary>
    public string Kind { get; set; } = "tool";

    /// <summary>Null on a guard step: no tool was called.</summary>
    public string? ToolName { get; set; }

    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }

    /// <summary><c>Ok</c>, <c>ToolError</c> or <c>Rejected</c>.</summary>
    public string Outcome { get; set; } = "Ok";

    public string? ModelId { get; set; }
    public string? PromptVersion { get; set; }

    /// <summary>Who said yes or no, on an approval step.</summary>
    public string? Approver { get; set; }

    public int DurationMs { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}
