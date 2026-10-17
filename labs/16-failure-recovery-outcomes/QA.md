# Lab 16 — Interview Q&A: failures, recovery, expected outcomes

---

## A. The in-doubt problem

**Q1. The agent calls refund, and the network drops before the answer. What now?**
A: The outcome is **unknown**: the refund may or may not have happened. Never guess, and never retry with a new key. The step was saved as `Started` with its idempotency key before the call. On recovery, send the **same** request with the **same** key. If the API did the refund, it replays the stored result. If it didn't, it does the refund now. Either way: exactly one refund.

**Q2. Why save the step *before* the call and not after?**
A: If you save only after, a crash between "API did it" and "we saved it" leaves no trace that you ever tried. The next run would try again with a new key and pay twice. Saving first (a write-ahead record) means you always know a call *might* have happened.

**Q3. What if the API has no idempotency support?**
A: Then before retrying, you must **query** for the effect ("is there a refund for payment 42 with my reference?"). That needs a client reference stored on the server side. Without either, you can't make a safe automatic retry, so the step goes to a human. That is why idempotency is a must for agent tools.

**Q4. Is a retry of a model call dangerous?**
A: No. A model call has no side effects: it only returns text or a tool request. Retry it with backoff. The danger is only in the **tool calls** it leads to, and those are protected by keys and approval gates.

## B. Retries, timeouts, circuit breakers

**Q5. Retry with backoff and jitter — why jitter?**
A: Without jitter, all clients that failed at the same moment retry at the same moment and hit the recovering server together (thundering herd). Random jitter spreads them out.

**Q6. Which errors do you retry?**
A: Timeouts, connection resets, 502, 503, 504 and 429 (respect `Retry-After`). Never retry 400, 401, 403, 404, 409 or 422: they will fail the same way again. A 409 can be the *answer* ("already refunded").

**Q7. What does a circuit breaker do?**
A: After many failures in a row it "opens" and fails fast for a while, without calling the broken service. Then it lets one test call through (half-open). It protects the failing service and stops your agent from burning its steps and money on a dead dependency.

**Q8. How did you do this in .NET?**
A: `Microsoft.Extensions.Http.Resilience` on the `HttpClient` (`AddStandardResilienceHandler()` or a custom pipeline). Polly v8 is inside it: total timeout, per-try timeout, retry with jitter, and a circuit breaker. Both the model client and the API client get one.

**Q9. A model stream breaks halfway through a tool call. What do you do?**
A: Throw the partial reply away and ask again. Never run a tool call whose arguments were not fully received and parsed. A half-received refund amount is the worst possible case.

## C. Memory after failure

**Q10. After a restart, how does the agent know what it already did?**
A: It doesn't remember. **The database does.** Load `AgentRun` and its `AgentStep` rows, rebuild the message history, and rebuild the pinned task state: goal, done steps, pending approvals, next expected step. The model gets that on the next call, as if nothing happened.

**Q11. What is the difference between this and Lab 15's trimming problem?**
A: It is the same root problem: the model's memory is only what you send. Trimming loses old messages on purpose. A crash loses everything in memory by accident. The fix is the same: durable step log plus pinned task state, rebuilt on every request.

**Q12. The MCP server restarted. What can go wrong?**
A: Tools may have changed (a new version, different schemas), and any state inside the server is gone. Re-list the tools and compare a hash of the schemas with the one stored in the run. If they differ, pause the run, because the plan was made for other tools.

## D. Expected outcomes

**Q13. What is a task contract?**
A: Before the agent starts, the code defines what success looks like: checkpoints after certain steps, plus a final **postcondition** ("payment 43 refunded total = 80.00, payment 42 untouched, 1 audit row"). The run is done only when the postcondition holds against real system state.

**Q14. Why not trust the model's "Done"?**
A: Models claim success they didn't reach: they say "refunded" when the call failed, or "saved" when nothing was written. Aperio has a harness scenario for exactly this (`false-write-claim`). In payments, check the **bank statement**, not the story.

**Q15. How do you check a checkpoint in the middle?**
A: After the step, read state through a read tool or the API and compare it with the expected values. Example: after "find", there must be exactly 2 captured payments with the same amount. If not, stop and ask a human. Don't let the agent improvise a refund on the wrong payment.

**Q16. What is plan drift, and what do you do about it?**
A: The agent calls something different from the next planned step. For reads, record it and allow it: the agent may need more facts. For writes, block it: no money moves outside the agreed plan. Aperio records drift without blocking (`plan-drift` scenario). Payments need the stricter rule for writes.

**Q17. Plan first, or let the agent decide as it goes?**
A: For tasks that move money: plan first (a workflow or a validated plan), then execute with checkpoints (Lab 13). For open questions ("why was this payment declined?"), let the agent explore with read-only tools.

## E. Testing failures

**Q18. How do you test network failures?**
A: With a fault-injecting `DelegatingHandler` in the `HttpClient` pipeline. It can add delay, return 503, throw a connection reset, or **forward the request and then throw away the response**. That last one is the only honest way to test the in-doubt case. Toxiproxy (a TCP proxy in Docker) can fault real connections.

**Q19. Which test would you show an interviewer first?**
A: "Drop the response after the refund was processed, then recover, then assert exactly one refund." It shows idempotency, write-ahead steps and recovery in one test. Most candidates have never tested that case.

**Q20. What is chaos engineering, in one line?**
A: You inject failures on purpose, in a controlled way, to prove the system handles them before production does it for you.

**Q21. Honest gap?**
A: Aperio has per-request timeouts, a health-check retry, abort handling and harness scenarios for false claims and plan drift. It has no general retry with backoff for model calls, and no write-ahead "in doubt" step state. This lab is where I built and tested those.


## Aperio: have you solved this before?

**Q-APERIO. "Have you solved this before?"**
A: Partly. Aperio has per-request timeouts, a health-check retry, abort handling, and harness scenarios that catch a model claiming success it didn't reach, and plan drift. It has no general retry with backoff for model calls and no write-ahead "in doubt" step state. This lab builds those and tests the dropped-response case.
Details and file references: the "Aperio comparison" section in this lab's `README.md`.
