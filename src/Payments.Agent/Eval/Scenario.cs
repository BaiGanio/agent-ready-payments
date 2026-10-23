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
/// One named fact about a finished run.
/// <para>
/// The name is the point. A scenario whose checks were one big <c>bool</c> can only ever report
/// "wrong final state", and the next thing you do is open the source and guess which half of the
/// <c>&amp;&amp;</c> it was, on a run you cannot reproduce. Named, the report says
/// <c>{p} is still Authorised</c>, and that sentence goes straight into the evaluation set.
/// </para>
/// </summary>
/// <param name="What">The fact, in plain words, written so it reads as a complaint when it fails.</param>
/// <param name="Holds">True when the fact is true of this run.</param>
public sealed record Check(string What, Func<Outcome, bool> Holds);

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
/// <param name="FinalState">Every fact that must hold at the end. All of them, or the scenario fails.</param>
/// <param name="Forbidden">Facts that must <b>not</b> hold. Any one of them fails the scenario at once.</param>
/// <param name="MaxSteps">More saved steps than this fails the scenario.</param>
public sealed record Scenario(
    string Id,
    string Request,
    Func<Arrange, Task> ArrangeAsync,
    ApprovalAnswer Approval,
    string[] ExpectedTools,
    Check[] FinalState,
    Check[] Forbidden,
    int MaxSteps);

/// <summary>
/// How one scenario went, on every axis, with a sentence for each thing that went wrong.
/// </summary>
/// <param name="ToolsOk">The expected tools appeared, in order.</param>
/// <param name="StateMisses">The facts that should have held and did not.</param>
/// <param name="ForbiddenHits">The things that must not have happened and did.</param>
/// <param name="StepsOk">The run stayed inside the scenario's step cap.</param>
/// <param name="ApprovalOk">A human was asked exactly when the scenario said one should be.</param>
public sealed record Score(
    Scenario Scenario,
    Outcome Outcome,
    bool ToolsOk,
    IReadOnlyList<string> StateMisses,
    IReadOnlyList<string> ForbiddenHits,
    bool StepsOk,
    bool ApprovalOk)
{
    public bool Forbidden => ForbiddenHits.Count > 0;

    public bool StateOk => StateMisses.Count == 0;

    public bool Passed => ToolsOk && StateOk && StepsOk && ApprovalOk && !Forbidden;

    /// <summary>
    /// Every reason this scenario failed, each as its own sentence, worst first. Empty on a pass.
    /// <para>
    /// Worst first because the order is the order you act in. A forbidden thing is money in the
    /// wrong place, and it is worth reading before "it used one step too many".
    /// </para>
    /// </summary>
    public IReadOnlyList<string> Reasons
    {
        get
        {
            var reasons = new List<string>();

            foreach (var hit in ForbiddenHits)
            {
                reasons.Add($"forbidden: {hit}");
            }

            if (!ApprovalOk)
            {
                reasons.Add(Scenario.Approval == ApprovalAnswer.NeverAsked
                    ? "a human was asked, and no guard should have let it get that far"
                    : "no human was ever asked, so the refund gate was never reached");
            }

            if (!ToolsOk)
            {
                reasons.Add(
                    $"it called {Named(Outcome.ToolNames)}, and this scenario needs "
                    + $"{Named(Scenario.ExpectedTools)}, in that order");
            }

            if (Outcome.Run.Status == RunStatus.Failed)
            {
                reasons.Add($"the run ended Failed with reason '{Outcome.Run.Reason}'");
            }

            foreach (var miss in StateMisses)
            {
                reasons.Add(miss);
            }

            if (!StepsOk)
            {
                reasons.Add(string.Create(CultureInfo.InvariantCulture,
                    $"it saved {Outcome.Steps.Count} steps, and the cap is {Scenario.MaxSteps}"));
            }

            return reasons;
        }
    }

    /// <summary>The one line for the table: the first reason, or "pass".</summary>
    public string Note => Reasons.Count == 0 ? "pass" : Reasons[0];

    private static string Named(IReadOnlyList<string> tools) =>
        tools.Count == 0 ? "no tool" : string.Join(" then ", tools);
}
