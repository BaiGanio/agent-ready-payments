# Lab 16 — Failures, recovery and expected outcomes

## What we solve
The network fails in the middle of an agent task. Afterwards the agent must know **which steps are done**, **which tool call is in doubt**, and whether the task reached the **outcome we expected**. It must not guess, and it must never pay twice.

## Simple example
Task: *"Refund the duplicate charge for order 1234."* The expected outcome is fixed **before** the agent starts:

| Checkpoint | Expected |
|---|---|
| after "find" | exactly 2 captured payments, same amount, same order, less than 10 min apart |
| after "propose" | 1 refund proposal for the **later** payment, for the full amount |
| after "confirm" | that payment's refunded total = its captured amount; the other payment is untouched |
| end | 1 audit row; the agent's answer names the refunded payment id |

Now the network breaks at the worst moment. The agent sends `refund_payment confirm`, the API refunds the money, and the connection drops **before the answer comes back**. The agent doesn't know if the refund happened.

- **Bad agent:** it retries with a *new* idempotency key, and the customer gets €80 twice. Or it starts over, forgets it already proposed, and proposes again.
- **Good agent:**
  1. Before the call, it saved the step as `Started`, with its idempotency key.
  2. On recovery, it finds that step still `Started`, so the step is **in doubt**.
  3. It sends the **same** request with the **same** key. The API answers with the stored result: "already refunded, here is the refund".
  4. It marks the step `Completed`, rebuilds its memory from the step log, and moves on.
  5. At the end, it **checks the outcome** by reading the real state from the API. It does not trust its own "Done".

## Everyday picture
**A bank transfer when the app freezes.** You press *Send*, the screen freezes, and you restart the app. A good bank app says *"Your transfer of €80 to Anna is already done"*. It doesn't send it again, and it doesn't say "try again". It knows because the transfer had a reference number from the start.

And the **outcome check** is the receipt: you don't trust "I'm sure I paid". You look at the bank statement.

## Failure cases covered

| # | What fails | What the agent does |
|---|---|---|
| 1 | Model call: timeout, 503, connection reset | Retry with backoff and jitter. A model call has no side effects, so it is safe to repeat. Stop after N tries, then pause the run. |
| 2 | Model call: the stream breaks halfway, with a half tool call | Throw the partial reply away. Never run a half-parsed tool call. Ask again. |
| 3 | Tool call: sent, answer lost (**in doubt**) | Write-ahead step record + the same idempotency key → the API replays the original result. |
| 4 | Read tool: fails | Retry. Reads are safe to repeat. |
| 5 | MCP server process dies | Restart it, list tools again, and compare the schema hash. If the tools changed, pause the run. |
| 6 | API down for a long time (circuit open) | Pause the run (`WaitingForRetry`, `NextWakeUtc`). Don't burn steps. |
| 7 | Agent process restarts | Rebuild messages and the pinned task state from `AgentRun` / `AgentStep` (Labs 09, 15). |
| 8 | Model claims "Done" but the outcome check fails | The run is **not** done. Report what is missing, or send it to a human. |
| 9 | Model calls a tool that is not in the plan | Reads: record it as drift. Writes: block them. |

## The shape (kept small)
- **Step states:** `Planned → Started → Completed | Failed | InDoubt`. The `Started` row (with the idempotency key) is saved **before** the side effect, like a write-ahead log.
- **Recovery routine:** on start, find every `Started` step that is older than its timeout. If it is a read, run it again. If it is a write, replay it with the same key.
- **Task contract:** a small C# record with checkpoints and a final **postcondition**, checked against API state, not against the model's words.
- **Resilience:** `Microsoft.Extensions.Http.Resilience` (Polly v8 inside) on the model and API `HttpClient`s: timeout, retry with jitter, and a circuit breaker.
- **Chaos tests:** a fault-injecting `DelegatingHandler` that can delay, return 503, or **drop the response after the request was sent** (the in-doubt case). Toxiproxy in Docker is optional for real network faults.

## Aperio comparison (our backbone)
- **Don't trust "Done":** Aperio's harness scenario `false-write-claim` checks that a model which *says* it saved a file it never wrote gets corrected at once (`tests/harness/scenarios/false-write-claim.json`).
- **Plan drift:** `plan-drift` records a call that differs from the plan's next step, without blocking it (`tests/harness/scenarios/plan-drift.json`). For payments we go further and block drift for writes.
- **Abort mid-chain:** `abort-mid-chain` covers stopping in the middle of a tool chain.
- **Honest gap:** Aperio's llama.cpp provider has a per-request timeout (`LLAMACPP_FETCH_TIMEOUT_MS`, default 300 s) and a health-check retry, but no general retry with backoff for model calls. There is no write-ahead "in doubt" step state either. This lab builds both.

## Why it matters for a payment platform
Payments run on unreliable networks: acquirers, schemes and banks all time out. "What if the answer is lost after the money moved?" is **the** classic payments question, and agents make it harder because they also lose their memory. If you can show write-ahead steps, key replay and outcome checks, you answer the ad's "state management" and "task execution" with something you proved under failure.

## Done when
- A chaos test drops the response of `refund_payment confirm` **after** the API processed it. After recovery there is exactly **one** refund, and the step is `Completed`.
- Kill the agent mid-task and restart it. It continues from the step log, and its pinned state shows the correct "next expected step".
- A model 503 storm opens the circuit. The run pauses, then resumes when the fault clears.
- A scripted model that says "Done" too early fails the postcondition check, and the run is not marked done.
- An unplanned write call is blocked. An unplanned read is recorded as drift.

Spec + failing tests: written when the lab starts.
