# Lab 13 — Interview Q&A: orchestrating durable, agentic workflows

---

## A. Workflow vs agent

**Q1. What is the difference between a workflow and an agent?**
A: In a workflow, **code** decides the order of steps, and the model fills bounded parts. In an agent, **the model** decides the next step in a loop. Workflows are predictable and easy to audit. Agents are flexible but harder to bound. (Anthropic's "Building effective agents" draws the same line.)

**Q2. Which one for a payments process like chargebacks?**
A: A workflow on the outside, because the steps, deadlines and money movements are fixed by scheme rules and must be auditable. Agent loops only *inside* one step, where the path is open: "find the evidence for this dispute". The model never owns the money decision.

**Q3. Name the common workflow patterns with LLMs.**
A:
- **Prompt chaining:** step A's output feeds step B, with checks between them.
- **Routing:** classify first, then send to a specialised path. Our reason classifier does this.
- **Parallelisation:** several calls at once, then combine (vote or split work).
- **Orchestrator-workers:** one model plans and hands sub-tasks to workers.
- **Evaluator-optimizer:** one step drafts, another checks, and they loop until it passes. Our draft is checked by plain code.

**Q4. How do you stop the model making a decision it shouldn't?**
A: Constrain its output. It must pick from a fixed list (enum schema). The code validates the answer. An invalid answer falls back to a safe default and a human flag. And the step that moves money is plain code behind a human approval, never a model call.

## B. Durable execution and state

**Q5. What does "durable" mean for a workflow?**
A: Its state lives in the database, not in memory. After a crash, deploy or reboot, it continues from the last finished step. It can wait days for a human or a deadline without holding a thread.

**Q6. How did you store the workflow?**
A: A `WorkflowInstance` row (`State`, `Data` as JSON, `NextWakeUtc`, version) and a `WorkflowStep` log (one row per finished step, with its result). The runner loads the instance, runs one step, and saves the new state and the step result in **one transaction**.

**Q7. How do several runner instances avoid taking the same workflow?**
A: They claim due rows with `SELECT … FOR UPDATE SKIP LOCKED` (Postgres) or `WITH (UPDLOCK, READPAST, ROWLOCK)` (SQL Server). Each runner gets different rows, and nobody waits. A version column catches the rare stale write.

**Q8. Why must every step be idempotent?**
A: A crash can happen after the step's work but before its result is saved. The step then runs again. If it is idempotent, the second run has the same effect: a tool call result already logged is reused, and a hold already placed is not placed twice.

**Q9. How does the workflow "sleep" for three days?**
A: It doesn't hold anything. It saves `State = WaitingForHuman` and `NextWakeUtc = deadline`. The runner only picks rows that are due, or that got an event (the decision). No thread and no timer in memory.

**Q10. How do you test something that takes 30 days?**
A: Inject `TimeProvider` (`FakeTimeProvider` in tests) and move the clock forward. Never call `DateTime.UtcNow` in workflow code.

**Q11. What is the transactional outbox, and do you need it here?**
A: When a step must change the database *and* send a message (email the merchant, notify the scheme), write the message to an `Outbox` table in the same transaction. A separate sender delivers it and marks it sent. Without it you either lose messages or send them for changes that rolled back. Yes, the "notify merchant" step needs it.

## C. Human in the loop, deadlines, compensation

**Q12. How do you build a human approval step?**
A: The workflow writes a pending decision with an expiry and stops. A human acts through an API (`POST /chargebacks/{id}/decision`). The decision is checked against the current state and version, and then the workflow resumes. Aperio's interrupt service works the same way: propose, expire, decide, claim (`lib/security/interruptService.js`, `008_agent_interrupts.sql`).

**Q13. What happens if nobody decides?**
A: The deadline fires. The workflow takes the safe default for the business, which here is accepting the loss before the scheme deadline passes. Deadlines are business rules, so they are explicit states, not exceptions.

**Q14. What is compensation, and why not a rollback?**
A: The steps already committed in separate transactions, sometimes in other systems, so there is nothing to roll back. A compensating step *undoes the effect*: release the hold, reverse a fee, send a correction. This is the **saga** pattern.

**Q15. Orchestrated saga vs choreographed saga?**
A: Orchestrated: one coordinator (our workflow) tells each step what to do, so the state is in one place and easy to audit. Choreographed: services react to each other's events, so there is less coupling but the flow is harder to see. For money flows I prefer orchestration.

**Q16. Why the dispute hold?**
A: If the merchant refunds a disputed payment and then loses the chargeback, the customer is paid twice. The hold makes the domain refuse the refund (`409 payment_under_dispute`). The workflow and the state machine work together.

## D. Tools and engines

**Q17. Would you build your own workflow engine in production?**
A: Usually not. Use a proven engine: **Temporal**, **Durable Task / Azure Durable Functions**, **Dapr Workflow**, **Elsa** (.NET), or saga support in **MassTransit** / **NServiceBus**. I built a small one in the lab to understand what they do: persisted state, idempotent steps, timers, retries, compensation.

**Q18. What does an engine like Temporal give you that your lab doesn't?**
A: Deterministic replay of workflow code, automatic retries with policies, timers at scale, versioning of running workflows, visibility tools, and a lot of hard edge cases already solved.

**Q19. What is workflow versioning, and why is it hard?**
A: A workflow started last week still runs on the old step logic when you deploy new code. You must support both, or migrate running instances on purpose. Store a version in each instance and branch on it.

**Q20. How do agent frameworks relate to this?**
A: Microsoft's newer agent tooling (Semantic Kernel and Microsoft Agent Framework, plus `Microsoft.Extensions.AI` underneath) offers agents *and* graph-style workflows. Know that they exist. The concepts are the same as this lab: state, steps, checkpoints, human approval.

## E. Reliability with a model in the loop

**Q21. The model is down or slow. What happens to the workflow?**
A: The step fails, and the runner retries it with backoff (model calls are idempotent: same input, a draft we can throw away). After N tries, it falls back to the manual path and flags the case. The deadline keeps running, so a dead model can't cause a missed deadline silently.

**Q22. How do you keep model output auditable?**
A: Store the input, the output, the model name and version, and the prompt version in the step log. A human can later see exactly why the workflow went the way it did.

**Q23. How do you test the model steps?**
A: In workflow tests, fakes return scripted answers: valid, invalid, slow, failing. The real model is checked separately with an evaluation set: dispute texts with expected reasons, scored for accuracy. It is the same split as Lab 09 and Aperio's harness.

**Q24. One sentence for the interviewer?**
A: "In payments I'd put a durable, code-driven workflow around the money and use agent loops only inside bounded steps. The model suggests, the workflow decides, and a human approves anything that moves money."


## Aperio: have you solved this before?

**Q-APERIO. "Have you solved this before?"**
A: Partly. Aperio has the building blocks — durable pending actions with expiry and an atomic claim, a scheduler that never overlaps, and plan checks — but no long-running business workflow with deadlines and compensation. This lab puts those blocks together into a durable saga.
Details and file references: the "Aperio comparison" table in this lab's `README.md`.
