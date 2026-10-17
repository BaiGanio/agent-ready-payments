# Lab 18 — Interview Q&A: system design for an agent-ready payment platform

---

## A. Starting well

**Q1. What do you do in the first five minutes?**
A: Ask and write down: who calls (internal agents, merchants' agents, apps), which operations (lookup, refund, chargeback), the numbers (peak requests per second, payments per day, read/write ratio), the rules (PCI, GDPR, DORA, audit retention), and the targets (latency, availability). Design follows numbers.

**Q2. Where do agents sit in your design, and why?**
A: At the edge, as callers — never inside the payment core. They go through the same gateway, scopes and API as any client. The core stays deterministic, and the agent layer can change fast without touching money logic.

**Q3. What is the single most important property of a payment write?**
A: It is **atomic and safe to retry**. One transaction changes the state and records the operation. An idempotency key makes a retry return the first result. Everything else builds on that.

## B. Consistency and transactions

**Q4. How do you make "refund" atomic?**
A: One stored procedure or one transaction: check the state and the amounts with a conditional `UPDATE … WHERE Status IN (…) AND RefundedAmount + @Amount <= CapturedAmount`, insert the operation row, insert the outbox message, and store the idempotency response. Commit once. `@@ROWCOUNT = 0` tells you why it failed.

**Q5. Why an outbox instead of "save, then publish"?**
A: If you publish after commit and crash in between, the event is lost. If you publish before, you can publish something that then rolls back. The outbox row commits with the data, and a relay publishes it at least once. Consumers are idempotent.

**Q6. Distributed transaction (2PC) across services?**
A: Avoid it. It couples availability and locks across services. Use a saga: local transactions plus compensation, coordinated by a workflow (Lab 13).

**Q7. Strong or eventual consistency — where?**
A: Strong for the money state of one payment (one row, one database, one transaction). Eventual for read models, reports, search indexes and notifications. The agent's read tools say how fresh their data is when it matters.

## C. Concurrency and scale

**Q8. Two callers refund the same payment at once. Walk me through it.**
A: Both hit the API. With different idempotency keys, both reach the proc. The conditional update lets only one succeed while the amount allows it. The other gets `refund_exceeds_captured`. With the **same** key, the unique index lets one insert win, and the other gets the replayed response. No app-level `if` is trusted for this.

**Q9. How does it scale out?**
A: Stateless API and agent hosts behind a load balancer. State only in the database and the bus. Partition hot data by merchant. Read replicas for lookups. A queue-based settlement pipeline with bounded concurrency. Rolling deploys with warm-up and readiness gating (Lab 17).

**Q10. Where is the bottleneck likely?**
A: The primary database on hot rows (a big merchant), lock contention in settlement, and the model layer (GPU or cost) for agents. Measure first. Then: shorter transactions, the right indexes, batching, queues, caching reads, and model routing by task.

**Q11. How do you keep agents from overloading the core?**
A: Per-agent rate limits and budgets at the gateway, scopes, step caps in the host, a circuit breaker, and cheap read models for agent lookups, so they don't hit the primary.

## D. The agent layer

**Q12. What is in the agent host?**
A: The loop (step cap, repeat breaker, cancellation), run state (`AgentRun` / `AgentStep` with write-ahead steps), the pinned task ledger, the context budget (trimming, offload, KV-cache-friendly prompts), and an MCP client to typed tools.

**Q13. What is the LLM gateway's job?**
A: Route each request to a model (local or cloud) by task, data class and cost. Redact personal data. Enforce budgets and rate limits. Log usage. Fall back when a provider is down. One place to enforce "card data never reaches a model".

**Q14. Local or cloud models?**
A: I'd ask what the company's policy is. The design supports both behind the gateway. Sensitive or high-volume tasks fit self-hosted models. Rare, complex tasks may justify a stronger model in the company's own cloud tenancy and region. DORA's third-party risk rules and PCI scope drive this as much as cost does.

**Q15. How do long processes like chargebacks fit?**
A: A durable workflow runner owns the process: state in the DB, steps idempotent, a human gate, deadlines and compensation. It calls agent steps for the bounded parts — classify, gather evidence, draft — through the same MCP tools.

## E. Safety, security, audit

**Q16. How do you keep card data out of the AI layer?**
A: The tools never return a PAN (only masked last 4 digits or a token). The gateway redacts as a second line. Logs and spans exclude prompts by default. The agent layer stays outside PCI scope by design.

**Q17. Prompt injection — what is your architecture answer?**
A: Assume it will happen sometimes. Limit the damage: least-privilege scopes per agent, untrusted content fenced as data, human approval for money, and the API enforcing every rule regardless of what the model says.

**Q18. How do you prove later who did what?**
A: One trace id from the agent to SQL, plus an append-only audit row per agent action with agent identity, the human behind it, the approval and the result. Keep audit separate from app logs, and never sample it.

## F. Failures

**Q19. The model provider is down. What happens?**
A: The gateway fails over to another model, or the agent host pauses runs (`WaitingForRetry`) with a clear status. Payments themselves are untouched — the core never depends on a model.

**Q20. The database primary fails over in the middle of a refund.**
A: The client gets an error or a timeout. The outcome is in doubt. The retry with the same idempotency key returns the stored result if the commit happened, or performs the refund if it didn't. Exactly once in effect.

**Q21. The bus is down.**
A: Outbox rows pile up in the database. Nothing is lost. The relay catches up when the bus returns. Alert on outbox lag.

## G. Trade-offs and evolution

**Q22. Stored procedures or app logic for the core?**
A: For hot, contended, set-based money operations: procs (one round trip, locking close to the data, DBA-tunable). For orchestration and integration: app code. Keep procs small, versioned, tested, and in source control.

**Q23. How would you introduce this into an existing .NET Framework platform?**
A: Strangler fig. Put the gateway and a new .NET API in front of the old one. Move one capability at a time (read lookups first, then refunds), behind the same contracts and tests. Agents only ever see the new, clean API.

**Q24. What would you build first?**
A: Clean single-job endpoints with ProblemDetails, idempotency on every write, and audit. Then read-only agent tools. Then one write tool with two-phase confirm. Then workflows. The AI part comes last because it depends on everything before it.

**Q25. What did this design teach you that a single lab didn't?**
A: (After the lab.) Typical answer: where the boundaries are matters more than any one technique. Money stays in a small, strict core. Everything clever stays at the edge, where a mistake can be refused.

## Aperio: have you solved this before?

**Q-APERIO. "Have you designed something like this before?"**
A: At a smaller scale, yes. Aperio has a written architecture, a coupling map with blast radius per module, six model providers behind one orchestrator, redaction before the model, and capacity data for local models (size, context, KV bytes per token). What it doesn't have is multi-node scale, a message bus, an outbox or a payment core. I designed those on paper here, using the patterns I tested in the labs.
Details and file references: the "Aperio comparison" table in this lab's `README.md`.
