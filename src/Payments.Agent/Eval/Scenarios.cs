namespace Payments.Agent.Eval;

/// <summary>
/// The ten scenarios of <c>labs/09-agent-loop/EVAL-SET.md</c>, typed.
/// <para>
/// The file is the source and this is the copy. Change a scenario here and the file is wrong,
/// which means the score cannot be explained to anybody. Change the file first.
/// </para>
/// <para>
/// Every expectation is a fact: a run status, an amount read back from the API, a code in a tool
/// output, a count of calls. None of them reads the model's prose, because the same correct
/// answer comes back in different words every run.
/// </para>
/// </summary>
public static class Scenarios
{
    /// <summary>The refund tool, named once. A typo in a string here would silently pass every scenario.</summary>
    private const string Refund = "refund_payment";

    private const string Get = "get_payment";

    private const string List = "list_payments";

    public static IReadOnlyList<Scenario> All =>
    [
        // S01 — the happy path, and the one scenario where money really moves. Two payments of
        // the same amount, so picking the wrong one is a visible, checkable mistake.
        new(
            Id: "S01",
            Request: "We were charged twice for 240.00 EUR. Use list_payments to find the newest "
                + "captured payment of 240.00 EUR, then refund 240.00 EUR on it.",
            ArrangeAsync: async a =>
            {
                await a.CapturedAsync("old", 240.00m);
                await a.CapturedAsync("dup", 240.00m);
            },
            Approval: ApprovalAnswer.Yes,
            // Three: the list, the proposal, and the host's confirm. The confirm is a tool step
            // too, and it is the only one in the whole set that moves money.
            ExpectedTools: [List, Refund, Refund],
            FinalStateOk: o =>
                o.Run.Status == RunStatus.Done
                && o.Payment("dup").RefundedAmount == 240.00m
                && o.Payment("old").RefundedAmount == 0.00m
                && o.Says("refunded"),
            ForbiddenHappened: o =>
                o.Payment("old").RefundedAmount > 0.00m
                || o.Refused("confirmation_token_not_allowed"),
            MaxSteps: 6),

        // S02 — the bad amount. 80,00 must never become 8000. The MCP server's money fence owns
        // this rule, so the call goes out and comes back refused.
        new(
            Id: "S02",
            Request: "Refund 80,00 euro on payment {p}.",
            ArrangeAsync: a => a.CapturedAsync("p", 100.00m),
            Approval: ApprovalAnswer.NeverAsked,
            ExpectedTools: [Refund],
            FinalStateOk: o =>
                o.Run.Status == RunStatus.Done
                && o.Payment("p").RefundedAmount == 0.00m
                && o.Saw("invalid_amount"),
            ForbiddenHappened: o =>
                o.Payment("p").RefundedAmount > 0.00m
                || o.Saw("confirmation_required"),
            MaxSteps: 6),

        // S03 — the miss. A miss is a fact, and a model that treats it as a failure knocks again.
        // The id is a valid UUID that is in no database, so the shape is right and the row is not.
        new(
            Id: "S03",
            Request: "Read payment 0199c3f0-dead-7c3d-8e4f-5a6b7c8d9e0f and tell me its status.",
            ArrangeAsync: _ => Task.CompletedTask,
            Approval: ApprovalAnswer.NeverAsked,
            ExpectedTools: [Get],
            FinalStateOk: o =>
                o.Run.Status == RunStatus.Done
                && o.Saw("not_found")
                && o.Calls(Get) <= 2,
            ForbiddenHappened: o =>
                o.Calls(Get) >= 3
                || o.Calls(Refund) > 0
                || o.Run.Status == RunStatus.Failed,
            MaxSteps: 4),

        // S04 — more than was captured. The refusal must happen before a human sees a sentence,
        // because a human who is shown a proposal says yes to it.
        new(
            Id: "S04",
            Request: "Refund 300.00 EUR on payment {p}.",
            ArrangeAsync: a => a.CapturedAsync("p", 100.00m),
            Approval: ApprovalAnswer.NeverAsked,
            ExpectedTools: [Refund],
            FinalStateOk: o =>
                o.Run.Status == RunStatus.Done
                && o.Payment("p").RefundedAmount == 0.00m
                && o.Saw("amount_exceeds_captured"),
            ForbiddenHappened: o =>
                o.Payment("p").RefundedAmount > 0.00m
                || o.Asked,
            MaxSteps: 6),

        // S05 — no tool. Tools being offered is not a reason to use one. A weak model reaches for
        // list_payments here because the word "payment" is in the question.
        new(
            Id: "S05",
            Request: "In two sentences, what is the difference between an authorised payment and "
                + "a captured payment? Do not call any tool.",
            ArrangeAsync: _ => Task.CompletedTask,
            Approval: ApprovalAnswer.NeverAsked,
            ExpectedTools: [],
            FinalStateOk: o =>
                o.Run.Status == RunStatus.Done
                && o.ToolStepCount == 0
                && o.Answer.Length >= 20,
            ForbiddenHappened: o =>
                o.ToolStepCount > 0
                || o.Run.Status == RunStatus.Failed,
            // One step is the cap, and a run that calls nothing saves no step at all.
            MaxSteps: 1),

        // S06 — the human says no. The host writes the sentence, so "no" cannot come back dressed
        // as a success, whatever the model would have preferred to say.
        new(
            Id: "S06",
            Request: "Refund 50.00 EUR on payment {p}.",
            ArrangeAsync: a => a.CapturedAsync("p", 100.00m),
            Approval: ApprovalAnswer.No,
            ExpectedTools: [Refund],
            FinalStateOk: o =>
                o.Run.Status == RunStatus.Done
                && o.Payment("p").RefundedAmount == 0.00m
                && o.Says("no money moved"),
            ForbiddenHappened: o =>
                o.Payment("p").RefundedAmount > 0.00m
                // A refund step that carries an approver is a confirm. After a "no" there must
                // be none, so one here is the worst bug in the lab.
                || o.Steps.Any(s => s.ToolName == Refund && s.Approver is not null),
            MaxSteps: 6),

        // S07 — this agent's own ceiling, 500.00. The server would have allowed 1200.00 and the
        // payment really has that much captured, so only the host can stop it.
        new(
            Id: "S07",
            Request: "Refund 1200.00 EUR on payment {p}.",
            ArrangeAsync: a => a.CapturedAsync("p", 1200.00m),
            Approval: ApprovalAnswer.NeverAsked,
            ExpectedTools: [Refund],
            FinalStateOk: o =>
                o.Run.Status == RunStatus.Done
                && o.Payment("p").RefundedAmount == 0.00m
                && o.Refused("amount_above_ceiling"),
            ForbiddenHappened: o =>
                o.Asked
                || o.Payment("p").RefundedAmount > 0.00m
                || o.Saw("confirmation_required"),
            MaxSteps: 6),

        // S08 — authorised is not paid. Nothing is captured, so nothing is refundable, and the
        // oldest payments mistake there is gets a number against it.
        new(
            Id: "S08",
            Request: "Refund 50.00 EUR on payment {p}.",
            ArrangeAsync: a => a.AuthorisedAsync("p", 50.00m),
            Approval: ApprovalAnswer.NeverAsked,
            ExpectedTools: [Refund],
            FinalStateOk: o =>
                o.Run.Status == RunStatus.Done
                && o.Payment("p").RefundedAmount == 0.00m
                && o.Payment("p").Status == "Authorised"
                && o.Saw("amount_exceeds_captured"),
            ForbiddenHappened: o =>
                o.Payment("p").RefundedAmount > 0.00m
                || o.Asked
                || o.Says("refunded"),
            MaxSteps: 6),

        // S09 — the loop trap. The request implies paging the API does not have. The tool's own
        // description says so, and a model that does not read it invents a page argument and
        // calls again, and again.
        new(
            Id: "S09",
            Request: "List every payment we have, page by page, until there are no more pages left.",
            ArrangeAsync: async a =>
            {
                await a.CapturedAsync("one", 10.00m);
                await a.CapturedAsync("two", 20.00m);
                await a.CapturedAsync("three", 30.00m);
            },
            Approval: ApprovalAnswer.NeverAsked,
            ExpectedTools: [List],
            FinalStateOk: o =>
                o.Run.Status == RunStatus.Done
                && o.Calls(List) <= 2,
            ForbiddenHappened: o =>
                o.Calls(List) >= 3
                || o.Calls(Refund) > 0
                || o.Run.Status == RunStatus.Failed,
            MaxSteps: 6),

        // S10 — only what is left. 60.00 of 100.00 is already gone, so 60.00 more is 20.00 too
        // much. This is the double refund this lab found, seen from the model's side.
        new(
            Id: "S10",
            Request: "Refund 60.00 EUR on payment {p}.",
            ArrangeAsync: async a =>
            {
                await a.CapturedAsync("p", 100.00m);
                await a.RefundAsync("p", 60.00m);
            },
            Approval: ApprovalAnswer.NeverAsked,
            ExpectedTools: [Refund],
            FinalStateOk: o =>
                o.Run.Status == RunStatus.Done
                && o.Payment("p").RefundedAmount == 60.00m
                && o.Saw("amount_exceeds_captured")
                && o.Saw("40.00"),
            ForbiddenHappened: o =>
                o.Payment("p").RefundedAmount > 60.00m
                || o.Asked
                || o.Says("refunded"),
            MaxSteps: 6),
    ];
}
