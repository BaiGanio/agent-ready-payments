using System.Globalization;

namespace Payments.Agent.Eval;

/// <summary>What the human channel does when the loop asks about a refund.</summary>
/// <remarks>
/// <see cref="NeverAsked"/> is not "answer no". It is an expectation: this scenario must stop
/// before a human is ever bothered. If the channel is called at all, the scenario fails. A model
/// that proposes a refund of money that is not there has already done the damage, because the
/// next thing that happens is a person reading a sentence and saying yes.
/// </remarks>
public enum ApprovalAnswer { Yes, No, NeverAsked }

/// <summary>What one arranged payment looked like after the run finished.</summary>
public sealed record PaymentFacts(
    string Id,
    string Status,
    decimal CapturedAmount,
    decimal RefundedAmount,
    decimal RefundableRemaining);

/// <summary>
/// Everything one scenario's run left behind, as facts. No expectation is allowed to read the
/// model's prose, because a model words the same correct answer differently every run and a
/// score that moves with the wording is not a score.
/// </summary>
/// <param name="Run">The saved run: its status, its reason, its answer.</param>
/// <param name="Steps">Every saved step, in order.</param>
/// <param name="Asked">Whether a human was asked. <see cref="ApprovalAnswer.NeverAsked"/> plus
/// this being true is a failure.</param>
/// <param name="Payments">The arranged payments, re-read from the API after the run, by handle.</param>
/// <param name="Seconds">Wall clock for the whole scenario, the arrange not counted.</param>
public sealed record Outcome(
    AgentRun Run,
    IReadOnlyList<AgentStep> Steps,
    bool Asked,
    IReadOnlyDictionary<string, PaymentFacts> Payments,
    double Seconds)
{
    /// <summary>The tool names that were really called, in order. A refused call counts: it was asked for.</summary>
    public IReadOnlyList<string> ToolNames =>
        [.. Steps.Where(s => s.Kind == "tool" && s.ToolName is not null).Select(s => s.ToolName!)];

    public int ToolStepCount => Steps.Count(s => s.Kind == "tool");

    public string Answer => Run.Answer ?? "";

    /// <summary>How many times one tool was called. The repeat guards are counted, not guessed.</summary>
    public int Calls(string tool) => ToolNames.Count(n => n == tool);

    /// <summary>True when any tool output carries this text: a code such as <c>not_found</c>.</summary>
    public bool Saw(string text) => Steps.Any(s =>
        s.OutputJson is { } json && json.Contains(text, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when any step was refused by the host with this code, so no tool ran.</summary>
    public bool Refused(string code) => Steps.Any(s =>
        s.Outcome == "Rejected" &&
        s.OutputJson is { } json &&
        json.Contains(code, StringComparison.Ordinal));

    /// <summary>One arranged payment as it stands now. A handle that was never arranged throws.</summary>
    public PaymentFacts Payment(string handle) => Payments[handle];

    public bool Says(string word) => Answer.Contains(word, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// One line of the evaluation set, as code.
/// <para>
/// It is a record of plain data and three small functions, so the whole set reads as a list and
/// a new scenario is one entry. The checks are functions and not strings, because a check
/// written as a string ends up being matched against the model's prose, which is rule 3's
/// whole point.
/// </para>
/// </summary>
/// <param name="Id">The id in <c>EVAL-SET.md</c>: <c>S01</c>, <c>S02</c>, ...</param>
/// <param name="Request">The words typed. <c>{handle}</c> is replaced with that arranged payment's id.</param>
/// <param name="ArrangeAsync">Creates this scenario's own payments, through the API.</param>
/// <param name="Approval">What the human says, or that no human may be asked.</param>
/// <param name="ExpectedTools">Tool names that must appear in this order. Extra reads are allowed.</param>
/// <param name="FinalStateOk">The facts that must hold at the end.</param>
/// <param name="ForbiddenHappened">True means something that fails the scenario at once happened.</param>
/// <param name="MaxSteps">More saved steps than this fails the scenario.</param>
public sealed record Scenario(
    string Id,
    string Request,
    Func<Arrange, Task> ArrangeAsync,
    ApprovalAnswer Approval,
    string[] ExpectedTools,
    Func<Outcome, bool> FinalStateOk,
    Func<Outcome, bool> ForbiddenHappened,
    int MaxSteps);

/// <summary>Why a scenario failed, or that it passed. Printed in the table's last column.</summary>
public sealed record Score(
    Scenario Scenario,
    Outcome Outcome,
    bool ToolsOk,
    bool StateOk,
    bool Forbidden,
    bool StepsOk,
    bool ApprovalOk)
{
    public bool Passed => ToolsOk && StateOk && StepsOk && ApprovalOk && !Forbidden;

    /// <summary>The short reason, for the table. The first thing that went wrong, not all of them.</summary>
    public string Note
    {
        get
        {
            if (Forbidden) return "forbidden";
            if (!ApprovalOk)
            {
                return Scenario.Approval == ApprovalAnswer.NeverAsked
                    ? "asked a human"
                    : "never asked a human";
            }

            if (!ToolsOk)
            {
                return "tools: " + (Outcome.ToolNames.Count == 0
                    ? "none"
                    : string.Join(" ", Outcome.ToolNames));
            }

            if (!StepsOk)
            {
                return string.Create(CultureInfo.InvariantCulture,
                    $"{Outcome.Steps.Count} steps > {Scenario.MaxSteps}");
            }

            if (!StateOk)
            {
                return Outcome.Run.Status == RunStatus.Failed
                    ? $"failed: {Outcome.Run.Reason}"
                    : "wrong final state";
            }

            return "pass";
        }
    }
}
