# Agent-Ready Payments Labs — Specification Input

Handoff file. Written 2026-10-17. Use this in a fresh session to produce the lab plan.
This file holds **decisions and constraints only**. It is not the plan.

---

## 1. The job

**Senior Full Stack .NET Engineer (AI)** at an international payment /
acquiring technology company. Remote, full-time. The goal is summarised in `README.md`.

Framing from the ad, verbatim in spirit: *"This is not a traditional Machine
Learning role. We are looking for a strong software engineer who understands how
AI agents execute tasks, manage state, interact with external tools, and
orchestrate workflows."*

### Responsibilities (condensed)
- Core backend for a high-volume payment platform.
- C# **.NET Framework** today, contributing to the move to **.NET Core**.
- Design backend services with **clear and deterministic interfaces** usable
  inside AI-driven / agentic workflows.
- **SQL Server stored procedures** optimised for highly concurrent, multithreaded
  production.
- Software design principles, patterns, **flow-based processing**.
- **TDD and BDD**, automated unit tests.
- Code reviews, engineering standards, Agile ceremonies.
- Evolve the platform as it increasingly interacts with agents and orchestration
  layers.

### Requirements (condensed)
- 4+ yrs C# / .NET Framework and/or .NET Core.
- Strong SQL Server: stored procedures, database-intensive backends.
- Highly concurrent / multithreaded backend design.
- Design patterns + flow-based processing.
- **Conceptual understanding of AI agent orchestration: state management, agent
  loops, task execution, tool-calling boundaries.**
- How APIs should be structured to be **reliable, deterministic tools** in agent
  workflows.
- TDD + BDD and their frameworks.
- Git with **Gitflow**.
- BSc CS or equivalent.

### Nice to have
- **SpecFlow** or similar BDD tooling.
- Payments / FinTech / banking background.
- Enterprise-scale, multi-server, highly multithreaded systems.
- Practical exposure to AI agents / agentic workflows.
- APIs designed for consumption by automated tools or orchestration layers.

### Five skill buckets to cover
1. C# / .NET (Framework → Core).
2. SQL Server: stored procedures, locking, concurrency.
3. Concurrency / multithreading / flow-based processing.
4. **AI agent orchestration** — agent loops, state, tool-calling boundaries,
   deterministic tool APIs.
5. TDD / BDD (SpecFlow or Reqnroll), Gitflow.

Bucket 4 is the candidate's differentiator: he has already **built** an agent
memory + MCP platform (Aperio). Most .NET applicants have nothing there.

---

## 2. Candidate context (answers given in this conversation)

| Question | Answer |
|---|---|
| C# freshness | **Current** — writes C# often. No warm-up lab needed. Added: because the role is a *wrapper over an existing system*, **raw SQL matters** — plan for **Dapper and ADO.NET**, not EF Core. |
| Goal of the labs | **Both** — interview credibility first, polish into a public portfolio repo later. |
| Timeline | **No interview booked yet** — just applied / about to apply. Calm pacing is fine. |
| Infra start point | **Plain Docker first**, k3s on the Pi 5 later. |
| Payments domain | **Near zero** — the vocabulary must be taught (auth, capture, refund, void, chargeback, idempotency, 3DS, settlement, acquirer vs issuer). |
| C# MCP server demo | **Yes, and it is the whole point** — but **keep it as simple as possible, nothing fancy**. The wrapper may be .NET Core; experiment with Dapper and ADO.NET in places. |
| TDD / BDD skill | **Both weak.** Follow the job ad's requirements. Exact testing approach to be **re-discussed once some implementation exists** — do not over-specify it up front. |
| Where code lives | **New separate repo: `/Users/lk/Projects/BaiGanio/agent-ready-payments`** (already created, `git init` done, nothing committed). Never inside the aperio repo. |
| Lab size | **~2–3 hours each, one topic per lab.** |
| Who writes the code | **The developer writes it.** The agent supplies the **spec plus failing tests**; developer implements against them. |
| Plan output | **`PLAN.md` in `agent-ready-payments`, plus a Mermaid diagram of the lab path.** |

---

## 3. Hard environment constraints (verified on this machine, 2026-10-17)

- **.NET SDK 10.0.302** installed at `/opt/homebrew/bin/dotnet`. Good.
- **Docker is installed** (`/Applications/Docker.app`) but the **daemon was not
  running**. Docker Desktop must be started before any container lab.
- **Mac is `arm64`** (Apple Silicon).
- **MSSQL does not run on ARM Linux.** There is no official SQL Server image for
  a Raspberry Pi 5. Azure SQL Edge was the ARM option and Microsoft retired it.
  Therefore:
  - **Postgres** → native ARM, runs on Mac and on the Pi 5 / k3s.
  - **SQL Server** → `mcr.microsoft.com/mssql/server:2022` is amd64 only; on this
    Mac it needs Docker Desktop **Rosetta x86 emulation**. It works but is slow.
    It must **not** be targeted at the Pi 5.
- **.NET Framework cannot run on macOS at all.** Framework-specific behaviour can
  only be discussed and read about, never executed locally. All runnable lab code
  is .NET (Core) 10.

---

## 4. Agreed scope and ground rules for the plan

- Labs, not an application. Small, self-contained, each ~2–3 h, one topic.
- Deliverable per lab: a short spec + failing tests from the agent; the developer
  writes the implementation.
- Both databases are in scope, with the split above: **Postgres** for the
  Docker/k3s and minimal-API path; **SQL Server** for the stored-procedure,
  locking and concurrency work the ad explicitly demands.
- Data access: **Dapper** primarily, with at least one **bare ADO.NET** exercise.
  EF Core is not a goal, but might be involve as well.
- The MCP server lab stays **deliberately minimal**.
- Payments vocabulary must be taught before or alongside the first domain lab.
- Infra sequence: **Docker Compose first**, k3s on the Pi 5 as a later, optional
  lab (Postgres only).
- Use the repo itself to demonstrate **Gitflow** (one branch per lab) — a cheap
  way to satisfy that requirement.
- Testing approach (xUnit depth, Reqnroll vs SpecFlow, how much BDD) is
  **intentionally left open** — revisit after the first implementation exists.

## 5. Candidate lab topics raised in conversation

Not finalised. Input for the planning session, in rough dependency order.

- Setup + payments glossary (Docker up, Postgres and MSSQL containers, solution
  layout, the words).
- Payment state machine via TDD — pure domain, no DB: authorise → capture →
  refund / void; illegal transitions rejected.
- Minimal API + Dapper over Postgres — explicit contracts, ProblemDetails errors,
  no hidden state (this is what "deterministic interface" means in practice).
- Idempotency — `Idempotency-Key` on POST, unique index, insert-first pattern,
  replay returns the same result; concurrency test with N parallel identical
  requests yielding one payment.
- SQL Server stored procedures + concurrency — capture proc with proper locking
  (`UPDLOCK`, `ROWLOCK`, `READPAST`), `rowversion` optimistic concurrency, create
  a deadlock on purpose and then fix it, read-committed snapshot.
- Flow-based processing — settlement batch pipeline with
  `System.Threading.Channels`: producer → N workers → writer, bounded channel,
  backpressure, cancellation, graceful shutdown, retry, poison message.
- Bare ADO.NET — `SqlBulkCopy`, streaming `DataReader`, why Dapper is a thin
  layer over this.
- BDD — Gherkin `.feature` files for refund rules, step definitions against the
  running API. (Scope to be decided later, see §4.)
- **C# MCP server** over the payment API — minimal: a few typed tools
  (`get_payment`, `list_payments`, `refund_payment` with confirmation), then drive
  it from an agent.
- Agent orchestration talking points — map the ad's bucket-4 vocabulary onto the
  Aperio code the candidate has already written.
- k3s on the Pi 5 — Postgres StatefulSet + the API as a Deployment. Optional, last.

---

## 6. Aperio as the evidence base for bucket 4

The plan should map each phrase in the ad to real, already-written Aperio code, so
interview answers are "I built this", not "I read about this".

| Ad phrase | Aperio evidence |
|---|---|
| agent loops | `lib/agent/index.js` — one orchestrator driving six provider loops |
| state management | `memories` / `self_memories` tables, `conversations` / `messages`, context assembly in `lib/context/` |
| tool-calling boundaries | MCP `ctx` shape in `mcp/index.js`; destructive-tool gating; `APERIO_ENABLE_SHELL` off by default; the `lib/routes/paths.js` allow-list gating every file operation |
| deterministic tool interfaces | MCP tool schemas; the validate-then-repair schema-check harness |
| task execution / orchestration | `agent_jobs` / `agent_runs`; the agent-loop regression harness in `tests/harness/` |
| token / context budget | `lib/context/` assembly and prompt-cache work |

Aperio paths are in `/Users/lk/Projects/BaiGanio/aperio`. **Read only** — the
plan must not modify the aperio repo, and other sessions may be active there.

---

## 7. What the fresh session should produce

1. `/Users/lk/Projects/BaiGanio/agent-ready-payments/PLAN.md` — the lab plan: ordered labs,
   each with topic, ~2–3 h size - might have each own folder and file, the skill bucket it serves, its deliverable, and
   its prerequisites.
2. A **Mermaid diagram** of the lab dependency path, inside `PLAN.md`. and then in each lab folder when each starts
3. No lab code yet. Specs and failing tests come per lab, on demand.
