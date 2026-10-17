# Lab 13 — Durable chargeback workflow (agentic workflow)

## What we solve
A business process that runs for **days**, has several steps, uses the model only where it helps, waits for a human, has a deadline, and survives restarts.

## Simple example
A cardholder disputes a €240 hotel payment: *"I cancelled, but they charged me anyway."* The issuer sends a **chargeback**.

1. **Received.** The workflow starts and puts a **dispute hold** on the payment. While the hold is on, nobody can refund that payment. A refund plus a lost chargeback would pay the customer twice.
2. **Classify.** *Model step.* The model reads the dispute text and must pick **one** reason from a fixed list: `cancelled_service`, `not_received`, `fraud`, `duplicate`, `other`. If its answer is not on the list, the workflow uses `other` and flags the case for a human. The model never decides alone.
3. **Gather evidence.** *Model step, read-only tools.* The Lab 09 agent loop, with only read tools (`get_payment`, `search_policies`, `get_booking_notes`) and a step cap.
4. **Draft the reply.** *Model step.* The model drafts the merchant's answer (the **representment**). A plain C# check then makes sure every claim in the draft cites an evidence id.
5. **Wait for a human.** The status is `WaitingForHuman`. The process can stop. The Mac can reboot. Three days later the merchant clicks *Submit* or *Accept loss*, and the workflow carries on from exactly this step.
6. **Deadline.** If nobody decides before the deadline, the workflow moves to `Expired` and accepts the loss automatically.
7. **End and undo steps:**
   - *Won:* the hold is released.
   - *Lost or expired:* the hold becomes a final debit.
   - *Cancelled* (the issuer withdraws): a **compensating step** releases the hold.

## Everyday picture
**A building project with a site manager.** The plan (foundation → walls → roof → inspection) is fixed. That is the **workflow**. A clever specialist (the model) is called in only for some jobs, such as "pick the right type of roof tile", and must choose from the catalogue. The site manager keeps a **logbook**. If he is ill for a week, the next manager reads the logbook and continues at "waiting for inspection", not at "dig foundation". The inspection has a deadline. If the build is cancelled, the scaffolding is taken down again: that is compensation.

## Workflow vs agent: the key idea of this lab
- **Workflow:** *the code* decides the steps. The model fills in bounded parts.
- **Agent:** *the model* decides the steps (Lab 09).
- In payments, the outer process is almost always a **workflow**. You use agent loops inside a step, not as the boss. You can say this in one sentence in the interview, and it matches the patterns Anthropic describes in "Building effective agents": prompt chaining, routing, orchestrator-workers and evaluator-optimizer.

## The shape (kept small)
- A `ChargebackWorkflow` state machine (like Lab 01), stored in a `WorkflowInstance` table with `State`, `Data` (JSON), `NextWakeUtc` and a version column.
- A `WorkflowRunner` `BackgroundService` takes due instances with `FOR UPDATE SKIP LOCKED`. That is the Postgres version of Lab 04's `READPAST`. It runs **one step**, saves the result, and sets the next wake-up time.
- Every step is **idempotent** and logged in `WorkflowStep`. After a crash, the step runs again safely.
- The model steps sit behind small interfaces (`IReasonClassifier`, `IEvidenceGatherer`, `IReplyDrafter`). Tests use fakes. The demo uses the local model from Lab 09.
- A human decision comes in through `POST /chargebacks/{id}/decision`.
- **Time:** use `TimeProvider`, so tests can jump 30 days ahead without waiting.

## Why it matters for a payment platform
The ad says *"orchestrate workflows"*, *"task execution"* and *"manage state"*. Chargebacks are a real, daily acquirer process. Few candidates can show a durable workflow with a human gate, a deadline and compensation, and fewer can explain when the model should *not* be in charge.

## Aperio comparison (our backbone)
| Case | Aperio | Where (verified 2026-10-17) | What this lab adds |
|---|---|---|---|
| Durable pending action with expiry | ✅ Has | `agent_interrupts` with `expires_at`, `expire()`, `claim` (`interruptService.js`, `008_agent_interrupts.sql`) | Workflow state that waits days for a human |
| Scheduled jobs that never overlap | ✅ Has | `lib/workers/agent-scheduler.js` (skip if already running, ~187) | A runner that claims due rows with `SKIP LOCKED` |
| Plans checked against execution | ✅ Has | `lib/agent/turn-planner.js`; harness `plan-valid-execution`, `plan-drift`, `plan-unknown-tool` | — |
| Multi-step saga with deadline and compensation | ❌ No | — | The whole chargeback workflow |
| Workflow vs agent split | ⚠️ Partly | jobs and interrupts around an agent loop | Code drives the steps; the model fills bounded ones |

## Done when
- The happy path works end to end: received → submitted → won → hold released.
- **Crash test:** kill the runner during "gather evidence", start it again, and it continues. There are no duplicate steps and no duplicate tool calls.
- **Human gate:** the workflow sleeps in `WaitingForHuman` and resumes when the decision arrives (in tests, a time jump with `TimeProvider`).
- **Deadline:** no decision before the deadline → `Expired` → loss accepted.
- **Compensation:** the issuer withdraws the chargeback → the hold is released.
- **Bad model output:** the classifier returns a reason that is not on the list → `other` and flagged. It is never trusted.
- **Refund blocked:** a refund on a payment under a dispute hold gets `409 payment_under_dispute`.

Spec + failing tests: written when the lab starts.
