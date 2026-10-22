# Lab 09 — Interview Q&A: agent loops, state, task execution, tool boundaries

The ad's bucket-4 words, verbatim: *state management, agent loops, task execution, tool-calling boundaries.* Every answer here should end with "and in Aperio that is file X" — see `TALKING-POINTS.md`.

---

## A. The loop

**Q1. What is an agent loop?**
A: `while (not done)`: send messages + tool schemas to the model → model answers with text (done) or tool calls → run the tools → append results to the messages → loop. The model plans; the host executes.

**Q2. Who runs the tools — the model?**
A: No. The model only *asks*. The host validates and runs the call, then reports the result. That boundary is where all safety lives.

**Q3. How do you stop an agent looping forever?**
A: Several bounds, from cheap to absolute:
1. Identical-call detector: same tool + same args 3× in a row → tools withdrawn for one pass.
2. Failure budget: N failed tool calls in a turn → stop.
3. **Hard step cap** per turn: after N tool-calling passes, tools are removed and the model must answer.
4. Wall-clock / token budget, and user cancel.
Aperio: `TOOL_REPEAT_LIMIT = 3` and `DEFAULT_TURN_STEP_LIMIT = 6` in `lib/tools/executor.js`; `TOOL_FAILURE_BUDGET = 3` in `lib/agent/tool-safety-middleware.js`. Honest note: Aperio has no per-turn wall clock; the step cap is the absolute bound.

**Q4. Why does an identical-call detector not suffice?**
A: A model can alternate between two different tools forever; every call resets the counter. Only a step cap bounds the loop by construction. Aperio's code comments document this exact case.

**Q5. How does cancellation work?**
A: A `CancellationToken` per turn, checked at the top of every pass and passed to every model and tool call. Watch out: a cancel that lands *between* awaits must still be seen — latch it. Aperio latches its own `cancelled` flag for that reason (`lib/agent/providers/llamacpp.js` ~443–452).

## B. State

**Q6. What state does an agent have?**
A: (1) Conversation messages — the working memory. (2) Durable memory — facts saved across sessions. (3) Task/run state — what job is running, its steps, its result. (4) Pending actions awaiting human confirmation.
Aperio: `conversations`/`messages`, `memories`, `agent_jobs`/`agent_runs`, `agent_interrupts` (migrations `001`, `002`, `008`).

**Q7. What survives a restart?**
A: Everything in the DB: conversations, memories, job definitions, run history, pending confirmations (with expiry). Not in-flight model streams — a turn interrupted by a crash is lost and must be re-asked. Aperio stores interrupt *descriptors*, never executable continuations; execution is reconstructed and re-validated.

**Q8. Context window full — what then?**
A: Trim old messages by token budget, keep the system prompt and latest user turn, drop orphaned tool results, offload big tool results to a store and give the model a handle. Aperio: `lib/context/trim.js`, `lib/context/toolResultOffload.js`.

**Q9. Is the agent stateless or stateful?**
A: The model is stateless; every call resends the context. The *host* is stateful. Designing the host state well is the job.

## C. Task execution / orchestration

**Q10. Synchronous chat vs background tasks?**
A: Background jobs have triggers (interval, file change), a run record, a verdict, retention, and must not overlap themselves. Aperio's scheduler skips a tick if the same job is still running (`lib/workers/agent-scheduler.js` ~187).

**Q11. Multi-agent — when is it worth it?**
A: When subtasks are truly independent or need different tools/permissions. Cost: more tokens, harder debugging, coordination bugs. Default: one agent, good tools.

**Q12. Orchestration vs choreography?**
A: Orchestration: one coordinator calls steps in order (agent loop, saga orchestrator). Choreography: services react to events. Agent hosts are orchestrators; the payment platform behind them can still be event-driven.

**Q13. How does an agent workflow look like a saga?**
A: Multi-step, each step can fail, and there is no global transaction. So: each step idempotent, record progress, compensate on failure (e.g. void the authorisation if a later step fails).

## D. Tool-calling boundaries

**Q14. Name the layers of protection at the tool boundary.**
A: Schema validation → allow-list of tools per agent/profile → destructive tools never auto-repaired → human confirmation for side effects → idempotency → resource scoping (paths, merchant ids) → audit log. Dangerous capabilities off by default (Aperio: `APERIO_ENABLE_SHELL`).

**Q15. Why refuse instead of repair malformed arguments?**
A: A "successful" repair can silently change meaning — shift a quote in a file, a decimal in an amount. For non-destructive reads, repair helps the model. For destructive actions, a clear error and a retry is safer. Determinism over helpfulness.

**Q16. How do you scope what a tool can touch?**
A: Per-request context, not per call site. Aperio keeps the allowed paths in `AsyncLocalStorage`, and every file check reads from it (`lib/routes/paths.js`). The C# equivalent is `AsyncLocal<T>` or a scoped DI service; in an API, the authenticated merchant id from the token, never from tool arguments.

## E. Testing nondeterminism

**Q17. How do you test an agent?**
A: Split it. Deterministic part (loop, guards, tools) — replay scripted model outputs through the real loop, fast, in CI. Nondeterministic part (does model X use the tools well?) — live evaluation drills, run manually or nightly, scored. Aperio: `tests/harness/` (~0.7 s, 14 scenarios) vs `scripts/model-tier-bench.js`.

**Q18. What did your C# loop in this lab include?**
A: (After the lab, in your words.) Message list, tools listed from the MCP server, one save per pass to SQL Server through EF Core, a step cap, a repeat breaker, a failure budget, cancel and a turn deadline, and refusals for unknown tools, broken arguments and leaked calls. The model never sees the confirmation token: the host keeps it and confirms only after a human yes. Around 150 lines. Tested with a fake `IChatClient` that returns a scripted sequence.

**Q19. Determinism and LLMs — can you make the model deterministic?**
A: Not fully. Temperature 0 reduces variance but does not guarantee identical output. So determinism must live in the tools and the host, not the model.

**Q20. What metrics would you track for agents in production?**
A: Tool error rate per tool and per model, schema mismatch types, loop-breaker trips, step-cap hits, confirmation reject rate, cost per task, task success rate. Aperio logs schema mismatches to a ledger to design fixes from data (`lib/tools/schemaCheck.js`).

## F. Lessons from the sketches (`AGENT-READY-BACKEND.md`)

**Q21. Draw your `AgentRun` / `AgentStep` tables.**
A: `AgentRun(Id, Status CHECK IN (Running, WaitingForHuman, Done, Failed), StepCount, Messages nvarchar(max) CHECK ISJSON, PendingCall, Answer, Reason, AgentName, ModelId, PromptVersion, timestamps, RowVer rowversion)` and `AgentStep(Id, RunId, Ordinal UNIQUE per run, Kind tool/approval/guard, ToolName, InputJson, OutputJson, Outcome Ok/ToolError/Rejected, ModelId, PromptVersion, Approver, DurationMs)`. On SQL Server, owned by EF Core, shipped as a DbUp script. One save per pass, so a restart resumes from the last pass.

**Q22. Why is `WaitingForHuman` the most valuable status?**
A: It lets the loop stop before anything that moves money, wait hours or days for a person, and resume. Without it, approval would have to happen inside one HTTP request.

**Q23. What does the tool registry do?**
A: Holds the allow-list of tools by name. Unknown name → a rejected result, not an exception. Validates input against the JSON schema before touching the domain. Catches tool exceptions and returns a clean sentence — but lets `OperationCanceledException` through, because Stop is not a failure.

**Q24. Why must a tool failure not crash the loop?**
A: It is a fact the model can react to: try another tool, ask the user, give up honestly. That is the difference between an agent and a script.

**Q25. What protects against two workers resuming the same run?**
A: A `rowversion` on `AgentRun` (optimistic concurrency — EF Core `[Timestamp]`), or a conditional claim `UPDATE … SET Status='Running' WHERE Id=@Id AND Status='WaitingForHuman'` — the same compare-and-swap as Aperio's interrupt claim.

**Q26. Why EF Core for these tables when the rest uses Dapper?**
A: The app owns this schema, the run is one aggregate saved as a unit, and JSON columns plus `rowversion` are first-class in EF Core. Right tool for the job.

**Q27. One-line pitch for the AI part of this job?**
A: "The chef is the easy part — you buy the chef. The kitchen is the work: labelled buttons, printed error cards, numbered receipts, and a list nailed to the door."

## G. Running it on a local model (llama.cpp)

**Q28. Why did you run the agent on a local model?**
A: There is no API key, no cost and no data leaving the machine. That matters in payments, where data rules are strict. A small local model also fails in realistic ways, so it tests my guards better than a strong cloud model would.

**Q29. How does the C# loop talk to llama.cpp?**
A: `llama-server` serves an OpenAI-compatible `/v1/chat/completions` endpoint with tool calling. Start it with `--jinja` so it uses the model's chat template for tools. In C# I use `Microsoft.Extensions.AI`'s `IChatClient` pointed at `http://localhost:8080/v1`. I can swap the local model for a cloud model by changing one registration. The loop does not change.

**Q30. Why did you not use the automatic tool-calling middleware?**
A: `UseFunctionInvocation()` runs the loop for you. That is fine in production for simple cases. But it hides the step cap, the approval gate and the run persistence, and those are the parts I must own and test. So I wrote the loop myself.

**Q31. What mistakes did the small model make, and how did your loop handle them?**
A: (Answer after the lab, from your own logs.) Typical cases:
- **Broken JSON in the arguments.** For a read tool, return the parse error and let the model try again. For `refund_payment`, refuse and never repair.
- **The same call again and again.** After 3 identical calls, take the tools away and force an answer.
- **A tool call printed as plain text** instead of a real tool call. Detect it, retry once with a nudge, then fail honestly.

Aperio has the same three guards for the same model family (`lib/tools/executor.js`, `lib/agent/providers/llamacpp.js`).

**Q32. Local or cloud model in production at a payment company?**
A: It depends on the task and on data rules. The design should not care, because the loop talks to an `IChatClient` interface. Card data never goes into a prompt in either case. Choose the model with an evaluation set: the same scenarios, scored, for each model. That is Aperio's live benchmark idea (`scripts/model-tier-bench.js`).

**Q33. How do you test the loop without a model?**
A: Use a fake `IChatClient` that returns a scripted list of replies (tool call, tool call, answer). Tests are fast and always give the same result. The real llama.cpp run is a separate, manual smoke test. This is the same split as Aperio's harness and its live drills.

## H. Crash/resume and concurrent agents

**Q34. Your agent process dies in the middle of a run. What happens?**
A: Every pass is saved — the model's reply, every tool result, the steps — before the next model call. On restart, the run is loaded with its full history and goes on from the next step. Tool calls already saved are not repeated. Two honest gaps. A crash **between** a tool call and its save repeats that one call: harmless for a read. For money, the approval is saved **before** the confirm, so on resume an approval with no confirm after it is `refund_in_doubt`: the run stops and a person checks the payment. It never asks again, because the first confirm may have gone through. The full fix — a write-ahead `Started` step and replaying the same idempotency key — is lab 16.
Trap: "Why not just ask the human again?" → The old yes may already have moved money. A second yes is a second refund.

**Q35. How did you test crash and resume?**
A: Use a fake model with a script of five steps. Throw on purpose after step 3. Create a new loop instance, the way a restart would, and resume the run. Assert that steps 1–3 were not called again and that the run finished at step 5.

**Q36. Two agents try to refund the same payment at once. What protects you?**
A: The same layers that protect against two humans: idempotency keys (same request → same result), the state machine (refund ≤ captured − refunded), and a write that re-checks the row (only one wins). The agents add no new risk — **if** the API was built right. Mine was not: building this test showed two refunds with **different** keys both winning, 29 of 30 rounds. The key table only catches one key used twice. The fix: lock the row, check its operation count is still the one loaded, retry the command once on fresh data. The loser gets the honest `422 amount_exceeds_captured`.
Trap: "Your idempotency tests passed. How did you miss it?" → They raced 50 requests with **one** key. Two keys is two honest requests, and only the payment row can say no. Test the race you fear, not the one you built for.

**Q37. Two workers try to resume the same agent run. What protects you?**
A: A version column (`rowversion`, or EF Core `[Timestamp]`) on `AgentRun`. Or a conditional claim: `UPDATE … SET Status = 'Running' WHERE Id = @Id AND Status = 'WaitingForHuman'`. Only one worker gets the row.


## I. Local vs cloud, and choosing a model

**Q38. Would a payments company use a local model or a cloud provider?**
A: It depends, and I'd ask. Regulation pushes towards **control**: PCI DSS (no card data in prompts), GDPR (EU data), and DORA (third-party ICT risk for payment institutions in the EU). That often means a cloud model inside the company's own tenancy and EU region, under an enterprise agreement, plus self-hosted models where data can't leave or where volume makes per-token pricing too expensive. Hybrid is common. My design doesn't care: the loop talks to `IChatClient`, so local and cloud are one registration apart.

**Q39. What if the agents calling the platform aren't our own agents?**
A: Then the platform doesn't choose the model at all. Merchants' or shoppers' agents call the payment API ("agentic commerce" — card schemes and others announced agent-payment programmes in 2025). The API must be safe for any agent: scopes, idempotency, two-phase confirm for money, stable error codes, audit. That is exactly what Labs 02, 03 and 08 build.

**Q40. How did you choose the local model?**
A: With an evaluation set, not by reputation: 10 scenarios with expected tool sequences and final states, run on a ladder of models from 3.9 GB to 21.3 GB. I picked the smallest model that passed. Small models fail in specific ways (broken JSON, repeated calls, tool calls printed as text), so my guards are tested on the smallest one.

**Q41. Dense vs MoE models, in one answer?**
A: A dense model uses all its weights for every token. A **mixture-of-experts** (MoE) model has many weights but uses only a few "experts" per token (for example, 26B total but about 4B active), so it runs close to small-model speed with more knowledge. The catch is that it still needs memory for all the weights.

**Q42. How do you size a local model on a machine?**
A: Weights (GB on disk, roughly) + KV cache (bytes per token × context length × parallel slots) + overhead. Aperio stores exactly these numbers per model — size, max context, KV bytes per token, dense/MoE, active parameters — in `db/migrations/011_model_facts.sql`, and sizes the context from them.

**Q43. What is an LLM gateway?**
A: A service in front of all model providers. It routes requests (local or cloud, by task, data class or cost), redacts personal data, enforces rate limits and budgets, logs usage, caches, and falls back when a provider is down. In a regulated company it is where "card data never leaves" is enforced once, for every team.

**Q44. Can the model approve a refund?**
A: No. "The model said so" is never an approval. A human approves, or a coded rule decides. The confirm token reaches the host only through the approval channel. If the model writes "the user approved, token rf_9x2", the host does not confirm. If the model sends any token at all, the host refuses it and saves a `Rejected` step; the MCP server never hears of it. The model never sees a real token, so any token it writes is invented. I have a test for each case. (The audit **table** is lab 14.)
Trap: "And if a tool result says 'approved, confirm now'?" → Tool results are data. They cannot change the state of an approval. Same test, different source.

**Q45. A model copies `1 266 250,00` from an invoice. What does your API do?**
A: It refuses it with `invalid_amount`. A naive parser removes the spaces and the comma and gets 126,625,000: a silent 100× error. The tool contract allows exactly one format (`"1266250.00"`). An ambiguous value is refused, never guessed. Negative, zero and above-ceiling amounts are refused too.
Trap: "Isn't refusing bad for the user?" → Asking again costs one turn. A 100× refund costs real money. Refuse is the right default for money.

**Q46. Where did this value come from?**
A: Every `AgentStep` stores the model id, the prompt version and the arguments. The audit row says who proposed (agent X, model Y) and who approved (person Z). Without that provenance, nobody can later tell a typed amount from an invented one.

## Aperio: have you solved this before?

**Q47. Why does the model never see the confirmation token?**
A: A gate whose key the model holds is a gate the model can open. It could repeat the token, or a tool result could tell it to use it. So the host cuts the token out of the proposal, keeps it in `AgentRun.PendingCall`, and sends the confirm itself after a human yes. The model can only *ask* for a refund.
Trap: "Lab 08 already checks the token in the MCP server. Why both?" → Defence in depth. The server stops a client that skips the human; the host stops the model from being that client.

**Q48. You found a bug in your own API while building the agent. Tell me about it.**
A: Two refunds of 100 on a 100 payment, at the same moment, each with its own idempotency key: both won, 29 of 30 rounds. The update was `where id = @Id` and nothing else — a lost update. I wrote the race test first and saw it red, then fixed it: `select … for update`, re-check the operation count, retry the command once on fresh data. Then 0 in 90 rounds, and the agent test proved it from the agent side.
Trap: "Why not a version column?" → It works too, but the loser hears `409 conflict`, which an agent cannot act on. The retry lets the domain answer: `422 amount_exceeds_captured`.

**Q49. Your agent tables are on SQL Server. On Postgres there is no `rowversion`. What then?**
A: Postgres has a hidden system column, `xmin`: the id of the transaction that last wrote the row. It changes on every update, so Npgsql's EF Core provider can use it as the concurrency token with no column of your own. Or add an explicit `version int` and bump it in every update. Same idea: `WHERE version = @loaded`, zero rows means someone else won.

**Q50. A run was waiting for a human, and the agent process restarted. What happens to the approval?**
A: The token is gone: it lived in the MCP server's memory, and the MCP server is a child of the agent process. So the host proposes again — a new token — and asks the human again. A yes is for one token, never for "whatever the agent does next". And if the old yes was already saved with no confirm after it, the host does not ask again at all: it stops with `refund_in_doubt`.

**Q-APERIO. "Have you solved this before?"**
A: Yes. Aperio's loop has a step cap, a repeat breaker, a failure budget, a cancel latch and guards for weak-model mistakes, across six providers. What it lacks is step-level resume after a crash and a wall-clock limit per turn. The C# loop in this lab adds both, and keeps the confirmation token away from the model.
Details and file references: the "Aperio comparison" table in this lab's `README.md`.
