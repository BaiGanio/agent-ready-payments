# Talking Points — Aperio as evidence for an agent-ready backend role

Source brief: `APERIO-TOUR.md`. Repo: `github.com/BaiGanio/aperio` (local: `/Users/lk/Projects/BaiGanio/aperio`).
Every path and line below was checked against the code on **2026-10-17** (commit `e44648d1`). Line numbers drift — re-check with `grep -n` before quoting one live.

**The one-sentence frame (say it early):**
> "I have built and run an agent orchestration layer end to end — the agent loop, the tool boundary, state persistence, cancellation, concurrency control, and a deterministic regression harness. It is in JavaScript; the design problems are exactly the ones in your job ad. The C# side I am showing in my labs repo."

---

## 1. The 90-second repo tour

Open these in this order. One sentence each.

| # | Open | Say |
|---|---|---|
| 1 | `AGENTS.md` → "Fragile / No-Touch Zones" (line 107) and "Module Coupling Map" | "This is the map of load-bearing modules and their blast radius — what a reviewer or an agent must check before touching each one." |
| 2 | `lib/agent/providers/llamacpp.js` line 450 | "This is one real agent loop: `while (true)`, with a cancel latch, retry-once guards for empty and leaked completions, a repeat breaker and a hard step cap." |
| 3 | `lib/tools/executor.js` lines 24 and 699–711 | "This is the tool boundary: destructive tools are a fixed set, and malformed arguments to them are refused, never repaired." |
| 4 | `lib/handlers/database/databaseHandlers.js` line 112 + `lib/security/interruptService.js` | "Side-effect tools are two-phase: propose, token, human approves, re-validate, digest check, atomic claim, execute. That is exactly how I'd put a refund behind an agent." |
| 5 | `db/migrations/008_agent_interrupts.sql` + `002_agent_jobs.sql` | "State lives in the DB: pending confirmations with a CHECK-constrained status machine, jobs and run history." |
| 6 | `tests/harness/README.md` | "And this is how it's proven: the real loop with a scripted fake model — deterministic, about 0.7 seconds, in CI." |

---

## 2. Ten stories (problem → decision → trade-off → file)

The tour brief asks for five. Stories 6–9 are strong bridges to payments, so they are included. Per-lab comparison: `APERIO-MAP.md`.

### Story 1 — Destructive tools are never argument-repaired
- **Problem:** Weak models emit broken JSON. A regex "repair" can produce valid JSON that silently shifts string boundaries — a corrupted file lands on disk.
- **Decision:** A fixed set of destructive tools (`write_file`, `edit_file`, `run_shell`, `forget`, …). For them `parseArgs` refuses and returns a precise parse error; the model must retry with valid JSON. Users can *add* tools to the set (`APERIO_EXTRA_DESTRUCTIVE_TOOLS`) but never remove the built-ins.
- **Trade-off:** Costs some tool-calling accuracy on weak models. One honest nuance: `db_execute` was deliberately taken *out* of the set (2026-08-14) because it is already two-phase confirm-gated — a human sees every field before it runs — and the refusal was costing real accuracy. The rule is "no unreviewed guess", not "maximum strictness".
- **File:** `lib/tools/executor.js` — `DESTRUCTIVE_TOOLS` line 24, `getDestructiveTools()` line 37, refusal at lines 705–711.
- **Payments line:** "`refund_payment` with an unparseable amount is refused, full stop."

### Story 2 — Testing a nondeterministic system in 0.7 s
- **Problem:** Loop behaviours (failure budget, offload, loop breakers) only exist when the whole loop runs. Checking them meant booting the app and chatting — slow, unrepeatable, skipped under pressure.
- **Decision:** A harness that runs the **real** `runAgentLoop`, real middleware, real path validation, real offload — and fakes only the model (scripted replay) and tool handlers. 14 JSON scenarios. A separate live benchmark answers a different question.
- **Trade-off:** It proves "my refactor didn't break the loop", not "model X is good". Two tools, two questions, kept apart on purpose; they share one scorer so a drill can move between them.
- **File:** `tests/harness/README.md`; scenarios in `tests/harness/scenarios/`; `npm run test:harness`; CI in `.github/workflows/ci.agent-harness.yml`; live side `scripts/model-tier-bench.js` (65 drills).
- **Payments line:** "Pin the nondeterministic part, test the deterministic rest. That's how I'd test agents near a payment API."

### Story 3 — Two database engines behind one store interface
- **Problem:** Zero-config single-user installs need SQLite; heavier installs need Postgres + pgvector.
- **Decision:** One store interface, two implementations; a factory picks the backend (explicit `DB_BACKEND`, else detect a running Postgres container, else SQLite). An unknown `DB_BACKEND` value falls back to SQLite rather than silently connecting somewhere the user never configured. 14 migrations per engine, kept in lockstep.
- **Trade-off:** Every schema change is written twice and must stay in sync — a real discipline cost, listed as a fragile zone in `AGENTS.md`.
- **File:** `db/index.js` (`resolveBackend()` line 87), `db/sqlite/store.js`, `db/postgres/store.js`, `db/migrations/` + `db/migrations-sqlite/`.
- **C# line:** "Repository behind an interface; in .NET that's `IPaymentStore` with a Dapper-Postgres and a Dapper-SqlServer implementation."

### Story 4 — The self-memory queue bug (F-R2-06)
- **Problem:** Two logical stores (`memories`, `self_memories`) shared one embedding queue. The queue's gate and its retry write both targeted `memories`. A self-memory id gated on the wrong store's signature and, on success, wrote its embedding where no self-memory row could ever be found. Silent, no error.
- **Decision:** A separate queue per store, each configured with its own `storeName` and `setEmbedding`.
- **Trade-off:** Two queues instead of one generic one. Correctness over reuse.
- **File:** `mcp/index.js` lines 79–89 (comment names the issue).
- **Payments line:** "Same shape as a stored procedure locking or updating the wrong row: everything 'succeeds', the data is wrong. The fix is to make the destination explicit, not inferred."

### Story 5 — One allow-list gates every file operation, carried by `AsyncLocalStorage`
- **Problem:** Many tools touch files; per-call-site checks drift. Symlinks can escape a folder.
- **Decision:** One containment function `isUnder()` that resolves symlinks via `realpathSafe()`. Read/write checks read the active allowed paths from an `AsyncLocalStorage` context set once per connection (`runWithPaths`), with the app-wide allow-list as fallback.
- **Trade-off:** Ambient context is invisible at call sites, so it must be documented and tested — it's a fragile zone in `AGENTS.md`.
- **File:** `lib/routes/paths.js` — `realpathSafe` 11, `getAllowlist` 107, `pathStorage` 155, `runWithPaths` 162, `isUnder` 259, `isReadPathAllowed` 267, `isWritePathAllowed` 273.
- **C# line:** "`AsyncLocal<T>` or a scoped service; in an API, the merchant scope comes from the auth token, never from tool arguments."

### Story 6 — Two-phase confirm with an atomic claim (the payments bridge)
- **Problem:** An agent proposes a write. Between proposal and execution, arguments could change, the token could be replayed, or two clicks could execute twice.
- **Decision:** `db_execute` proposes and returns a token (5-minute TTL); nothing runs. On approve: re-validate the arguments, compare a SHA-256 **digest** of the canonical arguments with the one stored at proposal, then **claim** with a conditional update — `UPDATE agent_interrupts SET status='claimed' … WHERE id=$1 AND status IN ('approved','edited') AND (expires_at IS NULL OR expires_at > $3)`. Only one caller can win the claim. The status set is a DB `CHECK` constraint.
- **Trade-off:** An extra round trip and a human in the loop for every write.
- **File:** `lib/handlers/database/databaseHandlers.js` (line 112, `proposeAction`, `commitAction`), `lib/security/interruptService.js` (`interruptDigest` 27, `createInterruptService` 74, digest check 201, `claimAndExecute` 216), `db/postgres/store.js` `claimAgentInterrupt` line 1151, `db/migrations/008_agent_interrupts.sql`.
- **Payments line:** "That conditional UPDATE is the same compare-and-swap I'd write in the capture stored procedure: `WHERE Status = 'Authorised'`. Same idea in the agent layer and the payment layer."

### Story 7 — The KV-cache break that cost 262 seconds
- **Problem:** To save tokens, Aperio removed 2 tool schemas from the request after a tool had already run (40 schemas → 38). llama.cpp puts tool schemas at the very front of the prompt, so this shifted everything after them.
- **Measured:** llama-server reused only `cache_n=12577` of `prompt_n=31067` tokens, and spent **262 s** re-reading the prompt on one turn. That pushed the turn past its 600 s limit and failed the test gate.
- **Decision:** Keep the tool list stable (`keepStable`). A repeated call to the pre-run tool costs one cached round trip (tens of ms) instead.
- **Trade-off:** The model sees 2 tools it doesn't need. A little more context, a lot less latency.
- **File:** `lib/agent/tool-profiles.js` ~55–68 (`filterPreExecutedTools`). Related: the cache warm-up in `lib/agent/index.js` (`warmCache`).
- **Payments line:** "Tokens are money and prefill is latency. I design prompts like cache keys: stable first, changes last."

### Story 8 — A check-then-act race on approvals
- **Problem:** Two approvals of the same pending memory could both pass the `status = 'pending'` check, and each would create a memory. It is the classic double-spend shape.
- **Decision:** Postgres: `BEGIN` → `SELECT … FOR UPDATE` → `UPDATE … WHERE status = 'pending'` → `COMMIT`. SQLite: the check and the insert run in one `db.transaction`.
- **Trade-off:** A short row lock on a rare operation. Correctness first.
- **File:** `db/postgres/store.js` 238 (`approvePending`), `db/sqlite/store.js` 669; the reason is written in the comment at `db/sqlite/store.js` ~560.
- **Payments line:** "Same bug as two threads capturing one payment. The fix is the same: let the database arbitrate — a row lock or a conditional update — not an `if` in the app."

### Story 9 — "Healthy" is not "ready": the model cold start
- **Problem:** llama-server's router loads a model lazily on the first chat request. Its `/health` turns green in about 0.5 s, **before** any model is loaded. So the user's **first message** paid the whole download and load — minutes — with progress visible only inside that one request.
- **Decision:** Move the cost to boot. `preloadMainModel()` fires a warm-up right after llama-server is up, which loads the model and fills the KV cache with the real system prompt. A process-wide status bus shows "downloading X — N of M GB" to every client, and replays the latest status to anyone who connects mid-load. Readiness is checked with the real model status (`isModelLoaded()`), not `/health`.
- **Trade-off:** Boot takes longer and uses the machine before anyone asks. Users get an honest banner instead of a chat that only looks ready.
- **File:** `lib/helpers/modelPreload.js` (header), `lib/helpers/modelProgress.js` 114, `lib/agent/providers/llamacpp.js` ~118 (the `/health` comment).
- **Payments line:** "After a deploy, I gate readiness on real warm-up — DB pool, model loaded, KV warm — not on 'process is up'. Otherwise the first customers pay the cold start, and their retries make it worse."

### Story 10 — ScroogeCorp: a DDD/CQRS slice in C# with money and a model in the loop
- **Problem:** In my own .NET API (BGAPI) I built a small invoice-financing context. A request goes Draft → Submitted → Approved or Rejected, and must never be approved above the counterparty's credit limit. A vision model (through Aperio) reads the invoice fields from uploaded documents.
- **Decision:** A DDD aggregate keeps the rule inside itself (`FinancingRequest.Approve()`). `Money` and `Iban` are always-valid value objects. CQRS with MediatR: EF Core on the write side, Dapper on the read side. The model sits behind interfaces (`ITradeDocumentExtractor`). A missing field parks the document as `NeedsReview`. The code never guesses.
- **Trade-off:** My own audit found the gaps: a parser that turns `1 266 250,00` into 126 million, no concurrency token (two reviewers can both "win"), and no stable paging order. Each one is now a written lab there (B03, B04, B11). This repo builds the same lessons in from the start (Labs 02, 04, 09).
- **File:** BGAPI `Core/BGAPI.Domain/ScroogeCorp/FinancingRequest.cs`, `Infrastructure/BGAPI.Infrastructure/ScroogeCorp/Extraction/InvoiceFieldParser.cs`, `Docs/labs/B11-untrusted-extraction/README.md`.
- **Payments line:** "I have built the C# side, found its money bugs myself, and written down how to fix each one, test first."

---

## 3. Likely questions → answer → file

| Question | Short answer | File |
|---|---|---|
| Walk me from a user message to a tool result. | WebSocket message claims a turn slot → orchestrator assembles context (trim, skills, tools) → provider loop calls the model → tool calls go through the hooked wrapper (safety middleware: budget, taint, repeat detection, untrusted-content fencing) → executor parses args (strict for destructive) → MCP tool runs → result (offloaded if large) appended → loop. | `lib/emitters/handlers/ws/turnLock.js`, `lib/agent/index.js` (`createAgent` 116), `lib/agent/providers/llamacpp.js` 450, `lib/agent/tool-hooks.js` (`callToolHooked` 303), `lib/agent/tool-safety-middleware.js`, `lib/tools/executor.js`, `mcp/index.js` |
| How do you stop an agent looping forever? | Three layers: identical-call breaker (3 in a row), failure budget (3 failed calls), and a hard per-turn step cap (6 tool passes) that removes tools and forces an answer. The cap exists because a model alternating between two tools resets the other counters. Honest: no per-turn wall clock. | `lib/tools/executor.js` (`TOOL_REPEAT_LIMIT` 514, `DEFAULT_TURN_STEP_LIMIT` 552, `resolveTurnStepLimit` 561), `lib/agent/tool-safety-middleware.js` (`TOOL_FAILURE_BUDGET` 8), `llamacpp.js` 788, 799 |
| How do you keep a model from calling a dangerous tool? | Off by default (shell needs `APERIO_ENABLE_SHELL=1`), tool profiles per agent, destructive args never repaired, two-phase confirm for side effects, path allow-list on every file op, writes after untrusted content are tainted. | `mcp/tools/shell.js` 76, 519; `lib/agent/tool-profiles.js`; `lib/tools/executor.js`; `lib/security/interruptService.js`; `lib/routes/paths.js`; `tool-safety-middleware.js` |
| What is your state model? What survives a restart? | DB: conversations/messages, memories, settings, jobs + runs, pending interrupts (with expiry), agent's private memory. Not in-flight streams. Interrupts store descriptors, never executable continuations. | `db/migrations/001_core.sql`, `002_agent_jobs.sql`, `006_self_memories.sql`, `008_agent_interrupts.sql`, `lib/helpers/sessions.js` |
| How do you test something nondeterministic? | Scripted-model harness through the real loop (deterministic, CI) + live benchmark (manual). Different questions, kept apart. | `tests/harness/README.md`, `scripts/model-tier-bench.js` |
| What happens when two things run at once? | Per-connection turn lock: a turn is claimed on **arrival**, dispatches are serialised in arrival order, a superseded turn is aborted. Scheduler skips a job tick if that job is still running. Separate embedding queues per store. | `lib/emitters/handlers/ws/turnLock.js` (`beginChatTurn` 78), `lib/workers/agent-scheduler.js` 187, `mcp/index.js` 79–89 |
| Worst bug you shipped? | The shared embedding queue writing self-memory embeddings to the wrong table (Story 4). Or the turn-lock race: two chats arriving during a third both waited on the same promise, the older registered last and reported "completed" though superseded — fixed by claiming at arrival. | `mcp/index.js` 79–89; `turnLock.js` header comment (T46) |
| This is JavaScript. Why believe you can do it in C#? | "The hard parts aren't syntax: tool contracts, idempotency, compare-and-swap, cancellation, bounded loops, test seams. I write C# regularly, and the labs repo shows the same designs in .NET 10 — state machine, Dapper, stored procs with locking, Channels pipeline, a C# MCP server." Then open `agent-ready-payments`. | `PLAN.md`, labs 01, 03, 04, 06, 08 |
| Where would this design fail at our volume? | Single process, single node. Locks are in-process (turn lock), not distributed. No load tests, no throughput numbers. Indexing shares the event loop. At payment-platform volume: state and locks move to the DB or a coordinator, idempotency keys in the DB, horizontal stateless hosts, queues between stages, real load tests and p99 targets. | `id/reference/tech-debt.md` ("Indexing shares the server's event loop", line 28) |

---

## 4. Concept map: ad phrase → Aperio → C# equivalent

| Ad phrase | Aperio file | C# / .NET equivalent |
|---|---|---|
| Agent loops | `lib/agent/providers/llamacpp.js` 450; six providers in `lib/agent/providers/` behind `lib/agent/index.js` | `while` loop over `IChatClient` (Microsoft.Extensions.AI) or Anthropic SDK; one `IModelProvider` interface, six adapters (Strategy/Adapter) |
| Loop termination | `TOOL_REPEAT_LIMIT`, `DEFAULT_TURN_STEP_LIMIT` (`executor.js`), `TOOL_FAILURE_BUDGET` | Step counter + `CancellationTokenSource.CancelAfter` for a wall clock |
| Cancellation | `cancelled` latch, `AbortController` in `llamacpp.js` 449–455 | `CancellationToken` passed everywhere; `CancellationTokenSource.CreateLinkedTokenSource` |
| State management | `001_core.sql`, `002_agent_jobs.sql`, `008_agent_interrupts.sql`, `lib/context/` | Tables + Dapper; `IOptions`/scoped services for per-request state |
| Context budget | `lib/context/trim.js`, `toolResultOffload.js` | Token-budgeted message list; large results to blob/table with a handle |
| Tool-calling boundary | `lib/tools/executor.js`, `lib/agent/tool-safety-middleware.js`, `lib/agent/tool-hooks.js` | MCP C# SDK tools + a middleware/filter pipeline (like ASP.NET Core filters) around tool calls |
| Deterministic tool interface | MCP zod schemas; `lib/tools/schemaCheck.js` (mismatch ledger) | `[McpServerTool]` methods with typed params → JSON Schema; ProblemDetails error codes |
| Human confirmation | `lib/security/interruptService.js`, `databaseHandlers.js` | Two-phase endpoint: `POST …/refund-proposals` → token → `POST …/confirm` |
| Atomic claim | `claimAgentInterrupt` conditional UPDATE (`db/postgres/store.js` 1151) | `UPDATE … WHERE Status=@expected` + `@@ROWCOUNT`, or `rowversion` |
| Ambient security context | `AsyncLocalStorage` in `lib/routes/paths.js` 155 | `AsyncLocal<T>` / scoped DI / claims from auth token |
| Turn lock | `lib/emitters/handlers/ws/turnLock.js` | `SemaphoreSlim(1,1)` per session (`ConcurrentDictionary<sessionId, SemaphoreSlim>`) |
| Embedding queue | `createEmbeddingQueue` in `mcp/index.js` 79, 85 | `Channel<T>` (bounded) + `BackgroundService` consumer |
| Store interface | `db/index.js`, two `store.js` | Repository interface, two Dapper implementations |
| Task execution / scheduling | `lib/workers/agent-scheduler.js` 84, overlap skip 187, interval 262 | `BackgroundService` + `PeriodicTimer`; Quartz.NET / Hangfire with `DisallowConcurrentExecution` |
| Untrusted content | `UNTRUSTED_CONTENT_TOOLS`, fencing in `tool-safety-middleware.js` 18 | Mark tool output as data; taint flag on later write calls |
| Orchestrate workflows (durable) | Pending-confirmation state with expiry and claim (`008_agent_interrupts.sql`, `interruptService.js`); scheduler with overlap skip (`agent-scheduler.js`) | Durable workflow table + runner with `SKIP LOCKED` / `READPAST`; Temporal, Durable Task, Dapr Workflow, Elsa, MassTransit sagas (Lab 13) |
| Workflow vs agent | Agent loop inside a turn; jobs and interrupts around it | "Code decides the steps, the model fills bounded steps" (Lab 13) |
| Observability | Lifecycle traces and events (`lib/agent/lifecycle-trace.js`, `turn-diagnostics.js`); breaker events `tool_repeat_break`, `tool_step_limit` | OpenTelemetry `ActivitySource` / `Meter`, GenAI `gen_ai.*` spans, trace context across MCP (Lab 14) — **not in Aperio** |
| Agent identity / least privilege | Tool profiles per agent (`lib/agent/tool-profiles.js`); taint gate on writes after untrusted content | API key per agent with scopes, enforced by the API; audit row per call (Lab 08) |
| Config discipline | `lib/config.js` single registry, `npm run gen:env:check` CI gate | `IOptions<T>` with `ValidateOnStart()`, one options class per section |

---

## 5. Known weaknesses — say them before they do

| Weakness | Honest answer |
|---|---|
| **It is not C#.** No .NET, no SQL Server, no stored procedures in Aperio. | "Correct. Aperio proves the agent-orchestration half. The `agent-ready-payments` repo is where I show the .NET half — stored procs with locking, Dapper, ADO.NET, Channels, a C# MCP server — all against your stack." |
| **No SQL Server dialect shown.** SQLite + Postgres only. | "The concurrency ideas are the same — conditional updates, unique constraints, isolation — but locking hints, RCSI and deadlock handling are engine-specific, so I practised them on SQL Server in Lab 04." |
| **Single-process concurrency.** Node, one event loop. No multi-server, no distributed locks, no leader election. | "Node gives you queues, locks and cancellation, not threads. I'll say that plainly. Multithreaded .NET work — thread pool, `SemaphoreSlim`, Channels, starvation — is Lab 06. Multi-server coordination I'd do through the database first." |
| **Not high-volume.** Self-hosted, small team. No load test, no throughput numbers. | "I have no throughput number to quote and I won't invent one. What I can show is the design choices that scale — idempotent writes, stateless hosts, DB-side arbitration — and I'd want to learn your load-testing practice." |
| **No BDD / Gherkin.** | "Aperio has unit, integration, e2e and a scenario-based harness, but no feature files. I learned Reqnroll in Lab 07; the given/when/then thinking is already in the harness scenarios." |
| **Solo project — no code-review culture.** | "True. I compensated with written rules: `AGENTS.md` (fragile zones, coupling map, verification per module), `CONTRIBUTING.md`, PR guard and many CI gates (`.github/workflows/ci.*`). I'd welcome real reviews." |
| **Open tech debt is public.** | "That's deliberate. `id/reference/tech-debt.md` is a debt register with the reason each item is still open. Two I'd name myself: agent-spec filesystem/memory scoping is not enforced at execution time yet (line 195), and the terminal standalone mode has no test coverage (line 737)." |
| **No OpenTelemetry.** Aperio has its own lifecycle traces and events, not OTel. | "Right. I wired OpenTelemetry end to end in .NET in Lab 14: one trace across agent, MCP server, API and SQL." |
| **No durable multi-step business workflow.** Aperio's jobs and interrupts are durable, but there is no long-running saga with compensation. | "That's Lab 13: a chargeback workflow with a human gate, a deadline, compensation and resume after a crash." |
| **No retry with backoff for model calls; no "in doubt" step state.** | "Aperio has per-request timeouts and a health-check retry. Retry with backoff, a circuit breaker and write-ahead in-doubt steps are what I built in Lab 16." |
| **Embedding queue is unbounded, no dead-letter table.** | "It retries with backoff and drops after 3 failures with a warning. In Lab 06 I built the bounded version with a poison queue." |
| **No per-turn wall clock.** | "The step cap is the hard bound; a single model request has its own timeout, but nothing bounds total turn time. In C# I'd add `CancelAfter` on the turn token." |

---

## 6. The pitch, and the question to ask them

**Open with this (first call):**
> "Agent-ready backends are about 80% ordinary API hygiene: small named endpoints, one error shape, safe retries. Add run tables and approval gates. The loop itself is about 40 lines. The chef is the easy part — the kitchen is the work."

Then, **only if it is true for you**, add: "I did that hygiene work on a legacy .NET system." Be ready to name the system and one concrete change. Then give the unique-index race (two threads both pass the `if`; only the index is safe). That line separates you from people who have only read about agents.

**The build order you would propose** (detail in `AGENT-READY-BACKEND.md` §8): clean single-job endpoints → one ProblemDetails contract → idempotency keys + unique index → `AgentRun`/`AgentStep` tables → tool registry → the loop → approval gates. Points 1–4 are the real job.

**Ask them these** (pick two or three):
> "Does the agent layer exist today, or am I building it — and how much of my week is it?"
> "Are the agents yours — support and operations — or your merchants' agents calling your API, or both?"
> "Do you run models yourselves, use a cloud provider in your own tenancy, or both? What does your PCI and DORA review say about it?"
> "Where does the money logic live today — stored procedures, the .NET services, or both?"

If the answer is "planned, some day", the role is mostly stored procedures and the Framework → Core move. That is fine — just know which job you are taking.

**Everyday pictures to keep ready** (from `AGENT-READY-BACKEND.md` §0): restaurant with a blindfolded chef (loop), the waiter's notepad (state), the list on the kitchen door (tool boundary), vending machine (deterministic tool), double-tap on the card reader (idempotency), hotel reception card (ProblemDetails), IKEA drawer label (tool description), "shall I put it through?" (approval gate).
