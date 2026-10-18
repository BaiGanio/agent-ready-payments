# Aperio Tour — Agent Briefing

Companion to `SPEC.md`. **Different job.** `SPEC.md` describes the C# labs to
build. This file tells an agent working **inside the Aperio repo** where to look,
so the developer can answer real engineering questions when interviewers open
the public GitHub repo in front of him.

- Target repo: `/Users/lk/Projects/BaiGanio/aperio` (public:
  `github.com/BaiGanio/aperio`).
- Paths and line numbers below were verified on **2026-10-17**. Line numbers
  drift — treat them as a starting point, not gospel. Re-check before quoting.

---

## 0. Rules for the agent doing this work

1. **Read only.** Do not modify, stage, commit or delete anything in the aperio
   repo. Other sessions may be active there. This is a research task.
2. **Do not start the server or any MCP process.** Read the code. See the
   "no stray state" rule in `AGENTS.md`.
3. **Verify every claim against the code before writing it down.** The whole
   point is that the developer can defend each statement in a live interview.
   A wrong claim is worse than a missing one.
4. **Write output to the labs repo, not to aperio**:
   `/Users/lk/Projects/BaiGanio/agent-ready-payments/TALKING-POINTS.md`.
5. **Be honest about gaps.** See §7. Interviewers probe weak spots. The
   developer must hear them from the agent first, not from the panel.

---

## 1. The honest framing (read this first)

Aperio is **Node.js ESM / Express 5 / SQLite + Postgres**. The job is
**C# / .NET / SQL Server**. Nobody should pretend otherwise.

The transferable claim is narrow and strong:

> "I have built and operated an agent orchestration layer end to end — the agent
> loop, the tool boundary, state persistence, cancellation, concurrency control,
> and a deterministic-tool regression harness. The language is JavaScript; the
> design problems are the ones in your job ad."

The job ad asks for *"solid conceptual understanding of AI agent orchestration,
including state management, agent loops, task execution, and tool-calling
boundaries."* Aperio is a working implementation of every one of those four
words. That is the entire value of this tour.

---

## 2. Where to look, per job requirement

### 2.1 "Agent loops"

| What | Where |
|---|---|
| Orchestrator entry point | `lib/agent/index.js` — `createAgent()` at ~line 116; the file is ~1100 lines |
| The actual loop | `lib/agent/providers/llamacpp.js` — `while (true)` at ~line 450; tool-call branch at ~line 694 |
| Six provider loops, one orchestrator | `lib/agent/providers/` — `anthropic.js`, `claude-code.js`, `codex.js`, `deepseek.js`, `gemini.js`, `llamacpp.js` |
| Per-turn planning | `lib/agent/turn-planner.js` (~620 lines) |
| Turn diagnostics | `lib/agent/turn-diagnostics.js`, `lib/agent/lifecycle-trace.js` |

**Things to extract:** how the loop terminates; the failure budget that stops a
runaway model; the empty-completion retry (`llamacpp.js` ~line 723); the
system-prompt-echo guard (~line 763); why six provider loops share one tool
schema. The "one orchestrator, six provider loops, same tool contract" shape is
a textbook Strategy/Adapter answer — and it is real, not a tutorial.

### 2.2 "Tool-calling boundaries" — the strongest section

| What | Where |
|---|---|
| The `ctx` object every tool depends on | `mcp/index.js` — `createContext()` at ~line 44, consumed at ~line 134 |
| Tool registrations | `mcp/tools/` — `memory.js`, `database.js`, `files.js`, `shell.js`, `web.js`, `github.js`, `image.js`, `codegraph.js`, `docgraph.js`, `wiki.js`, `self-memory.js`, `self-wiki.js`, `data.js`, `extraction.js` |
| Destructive-tool floor | `lib/tools/executor.js` — `getDestructiveTools()` at ~line 37 |
| **Refuse-to-repair rule** | `lib/tools/executor.js` ~lines 700–710: malformed args for a *non*-destructive tool may be regex-repaired; for a destructive tool the call is **refused**, never guessed |
| Argument/schema mismatch logging | `lib/tools/schemaCheck.js` (validate-then-repair harness) |
| Tool profiles / allowed sets | `lib/agent/tool-profiles.js` (~580 lines), `lib/agent/tool-safety-middleware.js`, `lib/agent/tool-hooks.js` (~850 lines) |
| Shell off by default | `mcp/tools/shell.js` + `APERIO_ENABLE_SHELL` in `lib/config.js` |
| Human-in-the-loop confirmation | `lib/security/interruptService.js` — `interruptDigest()` ~line 27, `createInterruptService()` ~line 74 |
| Permissions | `lib/security/agentPermissions.js` |

**Why this is the money section.** The job ad says: *"how backend services and
APIs should be structured to serve as reliable, deterministic tools within AI
agent workflows."* Aperio has an answer with teeth:

- A tool is a **typed schema**, not a free-text prompt.
- A malformed call to a destructive tool is **refused**, not repaired. Determinism
  beats helpfulness at the boundary.
- Dangerous tools are **off by default** and must be switched on by config.
- Destructive operations route through a **confirmation digest** before they run.

Map that straight onto a payments API: `refund_payment` is the destructive tool.
It needs a typed contract, an idempotency key, a confirmation step, and an
absolute refusal to guess an amount it could not parse.

### 2.3 "State management"

| What | Where |
|---|---|
| DB factory, two backends | `db/index.js` — explicit `DB_BACKEND`, else auto-detect, else SQLite (~lines 88–114) |
| Store implementations | `db/sqlite/store.js` (~1590 lines), `db/postgres/store.js` (~1395 lines) — same interface, two engines |
| Schema | `db/migrations/` and `db/migrations-sqlite/` — **14 files each, kept in lockstep** |
| Conversation + memory state | `001_core.sql` (memories, wiki, conversations, messages, settings) |
| Job / run state | `002_agent_jobs.sql` → `agent_jobs`, `agent_runs` |
| Agent's own private store | `006_self_memories.sql`, `007_self_wiki.sql` |
| Pending-confirmation state | `008_agent_interrupts.sql` |
| Context assembly and trimming | `lib/context/trim.js`, `lib/context/toolResultOffload.js`, `lib/context/ragStore.js`, `lib/context/artifactStore.js` |
| Session state | `lib/helpers/sessions.js` (~680 lines) |

**Things to extract:** the two-backend store interface is a clean
Repository-pattern answer, and the lockstep-migrations rule is a real operational
discipline worth describing. `toolResultOffload.js` is the "an oversized tool
result must not flood the context window" problem — a backpressure story.

### 2.4 "Task execution" / orchestration

| What | Where |
|---|---|
| Scheduler | `lib/workers/agent-scheduler.js` — `createAgentScheduler()` ~line 84, interval triggers ~line 262 |
| Run pruning (retention) | `lib/workers/agent-run-prune.js`, `lib/workers/session-prune.js` |
| Background job tools | `lib/workers/background-job-tools.js` |
| Multi-agent | `lib/workers/roundtable.js`, `lib/agent/spawn.js` |
| Skill matching during context assembly | `lib/workers/skills.js` |
| Job spec / deliverables | `lib/agent/job-spec.js`, `lib/agent/deliverables.js`, `lib/agent/artifact-lifecycle.js` |

### 2.5 Concurrency — the bridge to the .NET half of the interview

This is where Aperio talks to the job's *"highly concurrent and multithreaded"*
requirement. Node is single-threaded, so the answer is about **queues, locks,
backpressure and cancellation**, not threads. Say that out loud; it reads as
clarity, not as a dodge.

| What | Where |
|---|---|
| Turn lock (one turn at a time per session) | `lib/emitters/handlers/ws/turnLock.js` |
| WebSocket handler | `lib/emitters/handlers/wsHandler.js` (~770 lines) |
| Embedding queues | `createEmbeddingQueue` used in `mcp/index.js` ~lines 79–86 — note the **separate queue for `self_memories`**, because the shared queue gated on the wrong store's signature and wrote rows where nothing could find them (issue F-R2-06) |
| Cancellation | `lib/security/interruptService.js`; the Stop-pressed-mid-await comment in `llamacpp.js` ~line 443 |
| Async context propagation | `lib/routes/paths.js` ~line 155 — `AsyncLocalStorage` for per-request allowed paths. This is the direct analogue of .NET's `AsyncLocal<T>` / ambient context. Good answer material. |

The self-memory queue bug is an excellent story: *two logical stores sharing one
queue, where the gate and the retry write both targeted the wrong table.* That is
a concurrency-correctness bug with the same shape as a stored procedure locking
the wrong row.

### 2.6 Testing — TDD / BDD / determinism

| What | Where |
|---|---|
| **Read this first** | `tests/harness/README.md` — deterministic agent-loop regression harness, `npm run test:harness`, ~0.7s, no network, no model, no MCP subprocess |
| Suites | `tests/unit/`, `tests/integration/`, `tests/e2e/`, `tests/harness/`, `tests/docint/`, `tests/browser/` |
| Mocks | `tests/mockDB.js`, `tests/mockStore.js` |
| Testing guide | `id/reference/testing.md` |
| Live model benchmark | `scripts/model-tier-bench.js` — 65 drills, nondeterministic, manual |

**The best testing answer in the repo** is the distinction the harness README
draws explicitly:

- *"Given fixed model output, did my refactor break the loop?"* → deterministic,
  0.7s, runs in CI.
- *"Can model X actually drive Aperio?"* → live, minutes, manual,
  nondeterministic.

That is the whole problem of testing a nondeterministic system, solved by
**pinning the nondeterministic part and testing the deterministic remainder**. It
is exactly what a payment company will need when they put agents near a payment platform, and
it maps cleanly onto TDD (the harness) vs BDD (the live drills).

### 2.7 Security and path validation

| What | Where |
|---|---|
| Every file operation gates here | `lib/routes/paths.js` — `getAllowlist()` ~107, `isUnder()` ~259, `isReadPathAllowed()` ~267, `isWritePathAllowed()` ~273, `realpathSafe()` ~11 |
| Config | `APERIO_ALLOWED_PATHS` in `lib/config.js` |
| Policy doc | `SECURITY.md` |
| Encryption at rest | `db/encrypt.js`, `APERIO_DB_ENCRYPT` (AES-256-GCM, key in OS keychain) |
| Redaction | `lib/privacy/`, `lib/agent/redaction-relay.js` |

Payments company → they care about this. "One allow-list gates every read and
write, symlinks resolved via `realpath`, and the active list travels with the
request through `AsyncLocalStorage`" is a credible answer.

### 2.8 Config and operational discipline

| What | Where |
|---|---|
| Single config registry | `lib/config.js` (~580 lines) |
| Precedence resolution | `lib/config-resolver.js`, `lib/config-sync.js` |
| Generated docs | `docs/config-reference.md`, `.env.example` via `npm run gen:env`, CI gate `npm run gen:env:check` |
| Architecture | `id/reference/architecture.md` |
| Known debt, stated openly | `id/reference/tech-debt.md`, `A2D.md` |
| Fragile-zone map + module coupling table | `AGENTS.md` |

`AGENTS.md` is worth showing on its own. A documented list of load-bearing
modules, their blast radius, and how to verify a change to each — that reads as
seniority. Same for keeping `tech-debt.md` honest instead of empty.

---

## 3. The five best stories to rehearse

Pick these over a general tour. Each is a real decision with a real reason.

1. **Destructive tools are never arg-repaired** (`lib/tools/executor.js` ~700).
   Determinism at the tool boundary, chosen over helpfulness.
2. **The deterministic agent-loop harness** (`tests/harness/README.md`). How you
   test a nondeterministic system in 0.7s in CI.
3. **Two database backends behind one store interface** (`db/index.js`,
   `db/sqlite/store.js`, `db/postgres/store.js`, lockstep migrations).
4. **The self-memory queue bug** (`mcp/index.js` ~79–86). Shared queue, wrong
   store signature, silent wrong-table writes. Found, isolated, fixed.
5. **One allow-list gating every file operation, carried by AsyncLocalStorage**
   (`lib/routes/paths.js`). Ambient security context done once, not per call site.

---

## 4. Questions they will probably ask

The agent should draft an answer for each, grounded in a named file.

- "Walk me through what happens from a user message to a tool result."
- "How do you stop an agent looping forever?"
- "How do you keep a model from calling a dangerous tool?"
- "What is your state model? What survives a restart?"
- "How do you test something that is nondeterministic?"
- "What happens when two things run at once?"
- "What is the worst bug you shipped in this, and how did you find it?"
- "This is JavaScript. Why should I believe you can do it in C#?"
- "Where would this design fail at our volume?"

That last pair matters most. Have real answers, not deflections.

---

## 5. Deliverable the agent should produce

Write `/Users/lk/Projects/BaiGanio/agent-ready-payments/TALKING-POINTS.md` containing:

1. **The 90-second repo tour** — the five or six files to open, in order, and the
   one sentence to say about each.
2. **Five stories** (§3), each as: the problem, the decision, the trade-off, the
   file.
3. **Question → answer → file** table for §4.
4. **A concept-mapping table**: each phrase in the job ad's bucket-4 requirement
   → the Aperio file that implements it → the C# equivalent the developer would
   reach for (e.g. `AsyncLocalStorage` → `AsyncLocal<T>`; the turn lock →
   `SemaphoreSlim`; the embedding queue → `System.Threading.Channels`; the
   store interface → a repository behind an interface).
5. **Known-weakness list** (§7), with a prepared honest answer for each.

Verify each file reference exists before writing it. Keep it short enough to
reread on the morning of the interview.

---

## 6. Order to read the repo in

A cold agent should not open 60 files. This order builds the picture fastest:

1. `README.md`, then `AGENTS.md` (coupling map and fragile zones).
2. `id/reference/architecture.md`.
3. `mcp/index.js` — the tool boundary and `ctx`.
4. `lib/tools/executor.js` — how a tool call is actually executed and gated.
5. `lib/agent/providers/llamacpp.js` from ~line 440 — one real loop.
6. `lib/agent/index.js` — the orchestrator around it.
7. `db/index.js` + `db/migrations/001_core.sql` + `002_agent_jobs.sql` — state.
8. `tests/harness/README.md` — how it is proven.
9. `lib/routes/paths.js` — the security gate.
10. `id/reference/tech-debt.md` — what is honestly still broken.

---

## 7. Known weaknesses to prepare for — do not hide these

The agent must confirm each against the repo and draft an honest reply.

- **It is not C#.** No .NET, no SQL Server, no stored procedures anywhere in the
  repo. Covering that is the entire purpose of the labs in `SPEC.md`.
- **No SQL Server dialect experience is demonstrated.** SQLite and Postgres only.
- **Single-process concurrency model.** Node, not threads. No multi-server
  coordination, no distributed locking, no leader election.
- **Not a high-volume system.** Self-hosted, single-user or small-team. No load
  test, no throughput number to quote. Do not imply one.
- **No BDD / Gherkin layer.** The job ad names SpecFlow. Aperio has unit,
  integration, e2e and the harness — no feature files.
- **Solo project.** No code-review culture to point at, which the ad asks about.
  Compensate with `AGENTS.md`, `CONTRIBUTING.md` and the CI gates as the
  substitute for a second pair of eyes.
- **Open tech debt is visible in the repo.** This is a strength if owned ("here
  is my debt register and why each item is still open") and a weakness if
  discovered.

---

## 8. What this file is not

Not a plan. Not lab code. Not a change to the aperio repo. It is a reading map
and a research brief, so a fresh session can produce `TALKING-POINTS.md` without
rediscovering the codebase.
