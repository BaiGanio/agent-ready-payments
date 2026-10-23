# Agent-Ready Payments Labs — Plan

Written 2026-10-17 from `SPEC.md`. Target role: **Senior Full Stack .NET Engineer (AI)** at a payment technology company. See `README.md` for the goal.

Rules from the spec, short form:

- Labs, not an app. One topic per lab. About 2–3 hours each.
- The agent writes the lab spec (`README.md`) and the failing tests. **You write the code.**
- Every lab folder has a `README.md` that opens with **"What we solve / Simple example / Why it matters for a payment platform"**. The full spec is added below that when the lab starts.
- Every lab folder has a `QA.md`: 10–24 interview questions with answers (and follow-up "traps") that the lab lets you answer from first-hand work. Read it before the lab. Say the answers out loud after the lab.
- All runnable code is **.NET 10**. .NET Framework cannot run on macOS, so Framework topics are paper-only (Lab 10).
- Data access: **Dapper** first, **bare ADO.NET** once (Lab 05), **EF Core as the alternative where it fits** (see "Dapper vs EF Core" below).
- **Postgres** for the API / Docker / k3s path. **SQL Server** (amd64 image under Rosetta) for stored procedures and locking. Never target SQL Server at the Pi.
- Testing tooling stays open until Lab 01 is done (see "Open decisions").

---

## 1. The labs

| # | Folder | Topic | Bucket | Size | Needs | Deliverable (what exists when done) |
|---|---|---|---|---|---|---|
| 00 | `labs/00-setup-and-glossary` | Docker up, Postgres + SQL Server containers, **empty lab databases + DbUp runner**, solution layout, Gitflow branches, **payments words** | 1, 5 | 2–3 h | — | `docker-compose.yml`, `src/Payments.Db` migrating empty DBs on both engines, `develop` branch, you can explain every word in `QA.md` |
| 01 | `labs/01-payment-state-machine` | Payment state machine via **TDD**. Pure domain, no DB. | 1, 5 | 2–3 h | 00 | `Payments.Domain` + green tests: authorise → capture → refund / void; illegal moves rejected |
| 02 | `labs/02-minimal-api-dapper` | Minimal API + **Dapper** over Postgres. Explicit contracts, ProblemDetails, no hidden state | 1, 2, 4 | 3 h | 01 | `Payments.Api` with `POST /payments`, `POST /payments/{id}/capture`, `GET /payments/{id}`; integration tests vs real Postgres |
| 03 | `labs/03-idempotency` | `Idempotency-Key`, unique index, insert-first, replay = same answer, N parallel requests → 1 payment | 2, 3, 4 | ~5 h | 02 | Two-phase key claim in the endpoints (`idempotency_key` table) + a concurrency test that fires 50 identical POSTs |
| 04 | `labs/04-sqlserver-procs-locking` | SQL Server **stored procedures**, `UPDLOCK`/`ROWLOCK`/`READPAST`, `rowversion`, a deadlock made on purpose and fixed, RCSI | 2, 3 | 3 h | 01, 00 (MSSQL) | `usp_CapturePayment`, `usp_ClaimNextBatch`; a test that deadlocks before the fix and passes after |
| 05 | `labs/05-bare-adonet` | Bare **ADO.NET**: `SqlCommand`, streaming `SqlDataReader`, `SqlBulkCopy`, TVPs. Why Dapper is a thin layer on top | 2, 1 | 2 h | 04 | Load 1M rows with `SqlBulkCopy`; stream them back with constant memory; same query in Dapper for comparison |
| 06 | `labs/06-flow-pipeline-channels` | **Flow-based processing**: settlement batch with `System.Threading.Channels` — producer → N workers → writer, bounded, backpressure, cancellation, retry, poison message | 3 | 3 h | 05 | `Payments.Settlement` worker; tests for backpressure, cancel, poison queue, graceful shutdown |
| 07 | `labs/07-bdd-refund-rules` | **BDD**: Gherkin `.feature` files for refund rules, step definitions against the running API | 5 | 2–3 h | 03 | `refunds.feature` (Reqnroll — SpecFlow's successor) running in `dotnet test` |
| 08 | `labs/08-mcp-server` | **The C# MCP server.** Minimal. Typed tools `get_payment`, `list_payments` (capped at 20), `refund_payment` (two-phase confirm). API keys with scopes, one merchant per key | 4 | ~6 h | 03 | `Payments.Mcp` project, talks to `Payments.Api` over HTTP; run by hand in the MCP Inspector, driven by a local model in Lab 09 |
| 09 | `labs/09-agent-loop` | Write a **tiny C# agent loop** (~150 lines) that uses a **local model on llama.cpp** and calls `Payments.Mcp`. It has a step cap and a destructive-tool rule. The whole flow runs offline | 4 | ~11 h (decided 2026-10-22) | 08 | `Payments.Agent` console app doing "refund the last captured payment" end to end against `llama-server`; run state on SQL Server via EF Core; two configured agents and a rehearsed live demo (`DEMO.md`); tests with a fake model |
| 10 | `labs/10-framework-to-core` | **.NET Framework → .NET Core**: paper lab. Read, classify, plan a port of a small Framework-style sample | 1 | 2 h | 02 | A migration note: what breaks, what replaces it, in what order |
| 11 | `labs/11-k3s-pi` *(optional)* | k3s on the Pi 5: Postgres StatefulSet + API Deployment. Postgres only | infra | 3 h | 02 | Manifests + the API answering from the Pi |
| 12 | `labs/12-semantic-search` *(optional)* | **Semantic search** for the agent: refund/chargeback policy snippets, embedded with a **local embedding model** on llama.cpp, stored in **pgvector**, hybrid with Postgres full-text. New MCP tool `search_policies` | 4, 2 | 3 h | 09 | Policies searchable by meaning; the agent cites a policy id before it proposes a refund; SQL Server 2025 `VECTOR` as a discussion |
| 13 | `labs/13-chargeback-workflow` | **Durable agentic workflow:** a chargeback process that runs for days. The code decides the steps, the model fills bounded steps (classify, gather evidence, draft). It has a human approval, a deadline, compensation, and resume after a crash | 4, 3 | 3 h | 09 (+ 12 for `search_policies`, optional) | `WorkflowInstance` runner (`SKIP LOCKED`), crash/resume, deadline and compensation tests with `TimeProvider` |
| 14 | `labs/14-observability-audit` | **Observability + audit for agents:** OpenTelemetry trace across agent → MCP → API → SQL, GenAI spans, agent metrics, audit rows linked by trace id | 4 | 2–3 h | 08, 09 | One refund = one trace across 3 processes; audit row with the same trace id; a test proves no card data or prompt text in spans |
| 15 | `labs/15-context-and-caching` | **Context and caching:** KV cache and stable prefixes (measured with `prompt_n` / `cache_n`), staged trimming, pinned task state, offloading big tool results, read-tool cache with write invalidation | 4 | 3 h | 09 | Cache hit ratio logged per step; prefix-hash test; 40-turn run that never loses the pending refund token |
| 16 | `labs/16-failure-recovery-outcomes` | **Failures, recovery and expected outcomes:** model and network retries, circuit breaker, write-ahead `Started` steps, **in-doubt tool calls** replayed with the same idempotency key, MCP restart, task contract with checkpoints and postconditions | 4, 3 | 3 h | 09, 03 (+ 15) | Chaos test drops the refund response after processing → exactly one refund; early "Done" fails the postcondition |
| 17 | `labs/17-cold-start` | **Cold start at every layer:** .NET (JIT vs ReadyToRun vs Native AOT), DB pool, EF model, MCP process, model weights, KV cache, agent context, data. Warm-up service, **live vs ready**, `startupProbe`, honest "starting up" status, no retry storm | 4, 1 | 2–3 h | 09, 14, 15 | Startup timeline cold vs warm; `/health/ready` false until warm; 10 parallel first requests → one warm-up, no timeouts |
| 18 | `labs/18-system-design-capstone` | **System design capstone:** an agent-ready payment platform on a whiteboard — components, data flow, consistency and transactions, concurrency, scaling, failure modes, LLM gateway (local vs cloud), security and audit. Mock interview | all | 3 h | 03, 04, 09, 13 (ideally all) | A one-page design doc + diagram in your own words; 45-minute mock interview with the agent as the interviewer |
| 19 | `labs/19-human-in-the-loop-ui` *(optional)* | **The human in the loop:** an approval screen for agent proposals. The person sees the exact action, evidence and expiry, then approves exactly that version (`If-Match`). Four-eyes, `payments:approve` scope (never on agent keys), no "edit" for money. Svelte 5; patterns reused from `scrooge-financing` | 4, UI | 3 h | 08, 09 (+ 20, 13) | Pending list + card against the real API; tests for stale (`412`), expired, double click, agent key `403`, `four_eyes_required` |
| 20 | `labs/20-etag-if-match` *(optional)* | **ETag and `If-Match`** on the Postgres API: a `version` column, `ETag` on `GET`, `412 stale_version` on a stale write, `304` on `If-None-Match`; lab 08's refund token bound to the version. Moved out of lab 04 on 2026-10-22 | 1, 4 | 2–3 h | 03, 04, 08 | Two clients read version 7: the first capture wins, the second gets `412 stale_version` |

| 21 | `labs/21-engine-parity` | **Engine parity:** the API runs on **either** Postgres or SQL Server, picked by one config line. The two missing SQL Server tables (`IdempotencyKey`, `ApiKey`), a `SqlServer*` set behind the four existing seams, the whole API suite as a `[Theory]` on the engine, and a CI gate that proves both | 1, 2, 5 | 4–5 h | 03, 06, 08 | `"Engine": "sqlserver"` + `dotnet run` serves every endpoint; one test suite green on both engines; two CI jobs required |

Buckets (from `SPEC.md` §1): **1** C#/.NET · **2** SQL Server · **3** concurrency / flow · **4** agent orchestration · **5** TDD/BDD/Gitflow.

### Dapper vs EF Core — where each one goes

Rule of thumb you can say in the interview: **EF Core when the app owns the schema and saves whole aggregates. Dapper / ADO.NET when the schema already exists, when stored procedures own the logic, on hot paths and for bulk work.** A payment core is usually the second case, so Dapper leads. EF Core still shows up where it is the better tool:

| Lab | Main tool | EF Core part | Why |
|---|---|---|---|
| 02 | Dapper | **Stretch:** a second `IPaymentStore` implementation in EF Core; the same integration tests run against both | Two engines behind one interface — the same story as Aperio's two-backend store |
| 03 | Dapper insert-first | ~~**Second implementation:** EF Core check + `DbUpdateException` catch on the unique index~~ **Dropped 2026-10-20** — QA only, see §8 | The unique key is the lesson, and the Dapper version shows it in full |
| 04 | T-SQL procs | EF Core optimistic concurrency (`[Timestamp]` / `rowversion` → `DbUpdateConcurrencyException`); calling a proc with `Database.SqlQuery<T>` / `ExecuteSqlAsync` | EF has no lock hints; know when it's the wrong tool |
| 05 | ADO.NET | Compare `AddRange` + `SaveChanges`, `ExecuteUpdateAsync` (set-based) and `SqlBulkCopy` on the same data | Know the numbers, not just the opinion |
| 06 | Channels + bulk writer | One rule only: one `DbContext` per unit of work via `IDbContextFactory`, never shared across workers | `DbContext` is not thread-safe |
| 09 | EF Core | `AgentRun` / `AgentStep` persistence on **SQL Server** (JSON column with `ISJSON`, `rowversion`) | The app owns this schema — EF's home ground |
| 10 | — | EF6 → EF Core migration path | Part of the Framework → Core move |

### The dedicated MCP server (`src/Payments.Mcp`)

One C# MCP server project for the whole repo. It is born in Lab 08 and stays small.

- It is a **separate process** that calls `Payments.Api` over HTTP. It does not touch the database. This keeps the tool boundary honest: the agent can only do what the API allows.
- Transport: **stdio**, official `ModelContextProtocol` C# SDK. Nothing else.
- Tools in Lab 08: `get_payment`, `list_payments`, `refund_payment` (propose → token → confirm).
- **Scope decided (2026-10-17): payment tools only.** No study/helper tools. Later labs may add one *payment* tool if it teaches something (for example `get_settlement_batch` after Lab 06). No tool is added "because we can".

### The whole flow, fully local (Lab 09)

No API key and no cloud. Everything runs on the Mac.

```mermaid
flowchart TD
    U["You<br/>(console)"] --> A["Payments.Agent<br/>C# agent loop"]
    A -- "chat + tool schemas<br/>OpenAI-compatible HTTP" --> L["llama-server<br/>local model (gemma-4-E4B)"]
    L -- "tool call or answer" --> A
    A -- "MCP over stdio" --> M["Payments.Mcp<br/>3 payment tools"]
    M -- "HTTP + Idempotency-Key" --> API["Payments.Api<br/>Minimal API"]
    API --> DB[("Postgres")]
    A -- "EF Core: AgentRun, AgentStep" --> SQL[("SQL Server")]
```

- **Model server:** `llama-server` (already installed: `/opt/homebrew/bin/llama-server`, build 10090). It serves an OpenAI-compatible `/v1/chat/completions` with tool calling. Tool calling needs the chat template, so start it with `--jinja`.
- **Model:** start with the one Aperio already uses, `unsloth/gemma-4-E4B-it-qat-GGUF:UD-Q4_K_XL`. 32 GB RAM also fits a larger tool-capable model. If the small one gets tool calls wrong too often, pick a larger one in Lab 09.
- **C# side:** `Microsoft.Extensions.AI` `IChatClient` (an OpenAI-compatible client pointed at `http://localhost:8080/v1`), plus the MCP C# SDK **client** to start `Payments.Mcp` and list its tools. **You write the loop yourself.** Do not use the auto tool-calling middleware (`UseFunctionInvocation()`), because that would hide the loop the lab is about.
- **Why a weak local model is a good thing here:** small models make real mistakes. They send broken JSON, repeat the same call, or print a tool call as plain text. Your loop must handle each one. Aperio met the same problems with this exact model (`id/reference/tech-debt.md`: "gemma-4-E4B tool-call leakage"). That gives you real stories, not theory.
- **No cloud model, not even as a second client** (decided 2026-10-21). Claude Code was the planned comparison client; it is dropped. The comparison is between rungs of the local model ladder below.
- **Not on the Pi.** The Pi 5 is too slow for a useful agent loop. Lab 11 stays API + Postgres only.

### The lab database (decided 2026-10-17)

Full plan: **`db/README.md`** (tables, which lab adds what, ER diagram, seed data, test setup).

- One small payments database that **grows lab by lab**. Each lab adds its own migration.
- **Postgres** `payments_labs` (with pgvector) for the API and agent flow. **SQL Server** `PaymentsLabs` for stored procedures, locking, bulk and settlement. The shared tables are kept in lockstep in both engines.
- **DbUp** runs numbered plain `.sql` files. The runner is `src/Payments.Db`.
- **Seeder** in the same tool: deterministic (seed 42), `small` (~1,000 payments, with named demo fixtures) and `large` (1,000,000 payments, for Labs 05–06).
- **Tests** get a clean database each time: Testcontainers (Postgres), one database per test class (SQL Server), and Respawn between tests.

### Local or cloud models? (stance, 2026-10-17)

**We don't know what the company uses. Don't guess in the interview — ask.** What we can say with confidence:

- **Regulation pushes towards control, not necessarily towards "local":** PCI DSS (card data must never reach a prompt), GDPR (personal data, EU residency), **DORA** (EU, applies to payment institutions since 17 Jan 2025: third-party ICT risk, including AI providers), and the EU AI Act for some uses.
- **A Microsoft-stack company often lands in the middle:** a cloud model in its *own* cloud tenancy and EU region (for example Azure OpenAI / Azure AI Foundry under an enterprise agreement), plus self-hosted models where data must not leave, or where volume makes per-token pricing expensive. **Hybrid is the common answer.**
- **The agents may not be the company's at all.** "APIs consumed by automated tools or orchestration layers" can also mean **merchants' or shoppers' agents calling the platform** (agentic commerce: card schemes and others announced agent-payment programmes and protocols in 2025). Then the platform doesn't pick the model — it must make its API safe for *any* caller's agent. Our labs already do that (scopes, idempotency, two-phase confirm, audit).
- **So the design is provider-agnostic:** the loop talks to `IChatClient`. A local `llama-server` and a cloud endpoint are one registration apart. An **LLM gateway** in front (routing, redaction, rate limits, cost, fallback) is a system design topic in Lab 18.

**Our labs run local** (no key, no cost, offline, realistic failures). **Which local model:** decided by data, not taste. Lab 09 builds a 10-scenario **evaluation set** and runs it on a ladder from Aperio's own catalog (`db/migrations/011_model_facts.sql`):

| Rung | Model | Size | Why try it |
|---|---|---|---|
| 1 | `unsloth/gemma-4-E4B-it-qat-GGUF` | 3.9 GB | Small and fast; known tool-call leaks (Aperio tech-debt) → good for testing guards |
| 2 | `unsloth/Qwen3.5-9B-GGUF` (Q4_K_M) | 5.3 GB | Still small; dense |
| 3 | `unsloth/gemma-4-26B-A4B-it-GGUF` | 15.8 GB | MoE, 4B active → near-small speed, bigger brain |
| 4 | `unsloth/Qwen3.6-35B-A3B-MTP-GGUF` | 21.3 GB | MoE, 3B active; the largest that fits 32 GB with room for KV cache |

Pick the **smallest rung that passes** the evaluation set. Keep rung 1 for guard tests.

### Concurrency and atomic transactions — where each is drilled

| Concept | Lab |
|---|---|
| Conditional update (compare-and-swap), `@@ROWCOUNT` | 04, 13 |
| Unique index as the arbiter (insert-first) | 03 |
| Pessimistic locks: `UPDLOCK`, `FOR UPDATE` | 04 |
| Queue claims: `READPAST`, `SKIP LOCKED` | 04, 13 |
| Optimistic concurrency: `rowversion`, EF `[Timestamp]` | 04, 09 |
| Deadlocks: cause, detect (1205), fix | 04 |
| Isolation levels, RCSI | 04 |
| One transaction for state + step log; transactional outbox | 13, 16 |
| Sagas and compensation (no distributed transaction) | 13 |
| Threads, `SemaphoreSlim`, Channels, backpressure, thread-pool starvation | 06 |
| Single-flight warm-up, thundering herd | 17 |
| Two agents, one payment; two workers, one run | 09 |
| In-doubt calls after a lost response | 16 |
| Putting it all together on a whiteboard | 18 |

### API shape (decided 2026-10-17)

**Minimal API.** No controllers. Small and grown on the fly — each pattern comes in only when a lab feels the pain it solves. That is also the interview answer: "I add structure when the code asks for it, not before."

| When | What comes in | Why then |
|---|---|---|
| Lab 02 | Minimal API + `MapGroup("/payments")`, endpoints in one static class per feature, ProblemDetails, **.NET 10 built-in validation** (`AddValidation()` + DataAnnotations) | Simplest thing that works |
| Lab 02 | **CQRS-lite:** writes go through command handlers → domain → repository; reads are separate query services that return DTOs straight from Dapper | The split is natural: writes need the state machine, reads don't |
| ~~Lab 03~~ Not yet | **FluentValidation** (via an endpoint filter) | **Deferred 2026-10-20:** the key check is three lines, so lab 03 felt no pain. It comes in the first lab whose rules get conditional |
| Lab 08–09 | Same handlers reused by the MCP server through the API | Proves the handlers are the real contract |

What we **don't** add:
- **MediatR** — plain handler classes injected into endpoints are enough. MediatR also moved to a commercial licence in 2025. Know it (most codebases have it), don't need it here.
- **Full CQRS** (separate read DB, event sourcing) — far too big for a lab; say when you'd use it (Lab 02 `QA.md`).
- It is the main demo for the interview: "here is a payment API shaped to be a deterministic tool, and here is an agent using it safely".

---

## 2. Dependency path

```mermaid
flowchart TD
    L00["00 Setup + glossary<br/>Docker · Gitflow · payment words"]
    L01["01 State machine<br/>TDD, pure domain"]
    L02["02 Minimal API + Dapper<br/>Postgres"]
    L03["03 Idempotency<br/>parallel POSTs → 1 payment"]
    L04["04 SQL Server procs + locking<br/>deadlock on purpose"]
    L05["05 Bare ADO.NET<br/>SqlBulkCopy · DataReader"]
    L06["06 Flow pipeline<br/>Channels · backpressure"]
    L07["07 BDD<br/>Reqnroll refund rules"]
    L08["08 C# MCP server<br/>Payments.Mcp"]
    L09["09 Agent loop<br/>local llama.cpp model"]
    L10["10 Framework → Core<br/>paper lab"]
    L11["11 k3s on Pi 5<br/>optional"]
    L12["12 Semantic search<br/>pgvector · optional"]
    L13["13 Chargeback workflow<br/>durable · human gate · saga"]
    L14["14 Observability + audit<br/>OpenTelemetry"]
    L15["15 Context + caching<br/>KV cache · trimming"]
    L16["16 Failure + recovery<br/>in-doubt steps · outcomes"]
    L17["17 Cold start<br/>every layer · live vs ready"]
    L18["18 System design capstone<br/>whiteboard · mock interview"]
    L19["19 Human-in-the-loop UI<br/>approval screen · optional"]
    L20["20 ETag + If-Match<br/>stale reads over HTTP · optional"]
    L21["21 Engine parity<br/>one API · either database"]
    TP["TALKING-POINTS.md<br/>Aperio evidence"]

    L00 --> L01 --> L02 --> L03
    L01 --> L04 --> L05 --> L06
    L06 --> L21
    L03 --> L21
    L08 --> L21
    L03 --> L07
    L03 --> L08 --> L09
    L02 --> L10
    L02 -.-> L11
    L09 -.-> L12
    L09 --> L13
    L12 -.-> L13
    L09 --> L14
    L09 --> L15
    L09 --> L16
    L03 --> L16
    L15 -.-> L16
    L15 --> L17
    L14 -.-> L17
    L13 --> L18
    L04 --> L18
    L16 -.-> L18
    L09 -.-> L19
    L13 -.-> L19
    L04 -.-> L20
    L08 -.-> L20
    L20 -.-> L19
    TP -.-> L08

    classDef core fill:#1f6feb,color:#fff,stroke:#1f6feb
    classDef ai fill:#8957e5,color:#fff,stroke:#8957e5
    classDef opt fill:#6e7781,color:#fff,stroke:#6e7781
    class L01,L03,L04,L18 core
    class L08,L09,L13,L14,L15,L16,L17,TP ai
    class L11,L12,L19,L20 opt
```

Blue = the labs that carry most interview weight on the .NET/SQL side. Purple = the AI differentiator. Grey = optional.

When a lab starts, its `README.md` gets its own small Mermaid diagram (the flow inside that lab).

---

## 3. Fast path (if an interview gets booked early)

Do these, in this order, and skip the rest until after:

1. **00** (1 h version: only the glossary + Postgres container).
2. **01** — state machine. Proves TDD.
3. **03** — idempotency (fold the minimum of 02 into it).
4. **04** — stored procedure + locking. The ad names this directly.
5. **08** — MCP server. The differentiator.
6. **13** (if time allows) — even a fake-model version shows "orchestrate workflows".
6b. **18** (one evening) — the whiteboard run-through. It is the best rehearsal for a system design round.
7. Reread `TALKING-POINTS.md` and every `QA.md` the morning of the interview.

---

## 4. Repo layout (target, grows lab by lab)

```
agent-ready-payments/
├── PLAN.md  SPEC.md  APERIO-TOUR.md  APERIO-MAP.md  TALKING-POINTS.md  AGENT-READY-BACKEND.md
├── docker-compose.yml          # Lab 00: postgres + mssql
├── Payments.Labs.sln
├── src/
│   ├── Payments.Core/               # Lab 04 (Result, no packages)
│   ├── Payments.Domain/             # Lab 01
│   ├── Payments.Migrations/         # Lab 09 (RepoRoot, Migrator); Result in lab 04
│   ├── Payments.Api/                # Lab 02–03
│   ├── Payments.Data.SqlServer/     # Lab 04–05 (procs, ADO.NET)
│   ├── Payments.Settlement/         # Lab 06
│   ├── Payments.Mcp/                # Lab 08 — the dedicated MCP server
│   └── Payments.Agent/              # Lab 09
├── tests/
│   ├── Payments.Domain.Tests/
│   ├── Payments.Api.IntegrationTests/
│   ├── Payments.Data.SqlServer.Tests/
│   ├── Payments.Settlement.Tests/
│   ├── Payments.Specs/              # Lab 07 (BDD)
│   ├── Payments.Mcp.Tests/
│   ├── Payments.Agent.Tests/        # Lab 09
│   └── Payments.Testing/            # Lab 09: shared fixtures (CF-033)
├── db/
│   ├── README.md               # the lab database plan
│   ├── postgres/               # numbered .sql migrations (DbUp)
│   └── sqlserver/              # tables + stored procedures (lockstep)
└── labs/
    └── NN-name/
        ├── README.md           # simple example + why (now); full spec (when the lab starts)
        └── QA.md               # interview questions + answers (now)
```

---

## 5. Gitflow in this repo

The repo itself is the Gitflow evidence.

- `main` — only tagged, "finished" states. Tag after each lab: `lab-01`, `lab-02`, …
- `develop` — integration branch. Created in Lab 00.
- `feature/lab-NN-short-name` — one per lab, branched from `develop`, merged back with `--no-ff` so the merge is visible in history.
- `release/x.y` — cut once, after Lab 08, to show the release step (bump, changelog, merge to `main` and back to `develop`).
- `hotfix/…` — only if a real bug shows up on `main`. Do not fake one.

---

## 6. How each lab runs

1. You say "start Lab NN".
2. Agent adds the spec to `labs/NN-…/README.md` (new words, acceptance criteria, Mermaid of the flow) and writes the **failing tests**.
3. You branch `feature/lab-NN-…`, make the tests green, commit.
4. You answer `QA.md` out loud (or to the agent, who grills you).
5. Merge to `develop`, tag.

---

## 7. Open decisions (decide later, not now)

| Decision | When | Default if nobody decides |
|---|---|---|
| ~~Unit test framework~~ | **Decided 2026-10-18** | **xUnit v3**, plain asserts, no assertion library. NUnit is common in older .NET Framework shops; mention you know both in the interview. |
| BDD tool: Reqnroll vs SpecFlow | Lab 07 | **Reqnroll**. SpecFlow is end-of-life; Reqnroll is its open-source fork with the same Gherkin and bindings. |
| How deep BDD goes | After Lab 03 | One feature file, 5–8 scenarios, against the real API. |
| ~~Integration test DB~~ | **Decided 2026-10-17** | Testcontainers for Postgres; compose SQL Server with one database per test class; Respawn between tests. See `db/README.md`. |
| ~~Migrations~~ | **Decided 2026-10-17** | DbUp with plain `.sql` files; EF Core maps onto existing tables (database-first). |
| ~~MCP server scope~~ | **Decided 2026-10-17** | Payment tools only |
| ~~API style~~ | **Decided 2026-10-17** | Minimal API; CQRS-lite (Lab 02) and FluentValidation (Lab 03) on the fly |
| ~~Agent for Lab 09~~ | **Decided 2026-10-17** | Own C# loop on **llama-server + local gemma-4-E4B** (fully local). No API key needed. No cloud model as a second client (2026-10-21). |
| Local model | Lab 09 | The smallest rung of the model ladder (see "Local or cloud models?") that passes the 10-scenario evaluation set |
| ~~Which hops get a pull request, and what gates them~~ | **Decided 2026-10-22** | **`develop` → `main` through a PR with a required build-and-test check; `feature/lab-NN` → `develop` stays a local `--no-ff` merge; the tag stays manual.** See §8 for the reasoning. Tracked as `CF-006`. |
| ~~How a test says whether CI can run it~~ | **Decided 2026-10-22** | **A trait, `[Trait("runs", "local")]`, on the tests CI *cannot* run; CI excludes them and runs the rest.** The tag marks the exceptions, not the rule — the earlier draft of this row had it the other way round, which would have meant tagging all 324 tests and skipping any test with a forgotten tag in silence. See §8 for the verified command and its two traps. |

---

## 8. Decided (read at the start of every session)

Decided things only. Open findings, fix-later items, questions and ideas live in [`CARRY-FORWARD.md`](CARRY-FORWARD.md), the inbox. An item never sits in both files.

- **Best payments bridge in Aperio:** `db_execute` is confirm-gated, then does an atomic claim — `UPDATE … WHERE status IN ('approved','edited')`. A capture stored procedure uses the same trick (`WHERE Status = 'Authorised'`). It is story 6 in `TALKING-POINTS.md`.
- **The MCP server** is `src/Payments.Mcp`. Lab 08 builds it with 3 tools. It calls the API over HTTP and never touches the database.
- **Sketches + everyday pictures** (restaurant, vending machine, card double-tap, hotel reception, IKEA label, approval gate) live in `AGENT-READY-BACKEND.md`. Reuse them in lab READMEs and in answers.
- **EF Core** is the alternative where it fits — see "Dapper vs EF Core" above.
- **Decided 2026-10-17 (agentic coverage, option A):** added **Lab 13** (durable chargeback workflow: workflow vs agent, human gate, deadline, compensation, resume) and **Lab 14** (OpenTelemetry + audit). Tightened Lab 08 (API key per agent with scopes + audit row per tool call), Lab 09 (crash/resume test + two-agents-one-refund test), Lab 12 (prompt-injection test).
- **Added 2026-10-17:** **Lab 15** (KV cache, trimming, pinned task state, offload, tool cache) and **Lab 16** (network failures, in-doubt tool calls, resume, task contracts with expected outcomes).
- **Added 2026-10-17:** **Lab 17 — cold start** at every layer (.NET, DB, EF, MCP, model, KV cache, agent context, data). Key Aperio facts: llama-server `/health` is green before the model loads; the first message used to pay minutes until `modelPreload.js` moved it to boot.
- **Stance 2026-10-17:** design provider-agnostic (`IChatClient`); labs run local; the model is picked by Lab 09's evaluation set over a ladder from Aperio's `011_model_facts.sql`. The open questions — local or cloud at the company, and whose agents call the platform — are `CF-001` and `CF-002`.
- **Added 2026-10-17:** optional **Lab 19 — human-in-the-loop UI** (approval screen). Reuses frontend patterns, not files, from `BaiGanio/scrooge-financing` (Svelte). Do it after Lab 13.
- **Added 2026-10-17:** **Lab 18 — system design capstone**, and a "concurrency and atomic transactions" index in this plan.
- **Rule from 2026-10-17: Aperio is the backbone.** Every lab README has an **"Aperio comparison"** table (has / partly / no, file, what the lab adds), and every `QA.md` ends with **Q-APERIO "Have you solved this before?"**. The overview is `APERIO-MAP.md`. Verify every Aperio claim against the code before writing it.
- **Lab database:** planned in `db/README.md`. It grows one migration per lab; the seeder makes deterministic demo and bulk data.
- **Every lab:** README opens with a simple example + an everyday picture; `QA.md` has many questions (more is better).
- **Decided 2026-10-17:** MCP server scope = **payment tools only** (`get_payment`, `list_payments`, `refund_payment`). No study tools.
- **Decided 2026-10-17:** Lab 09 runs the **whole flow locally**: C# loop → `llama-server` (gemma-4-E4B) → `Payments.Mcp` → `Payments.Api` → Postgres. See "The whole flow, fully local".
- **Added 2026-10-17:** optional **Lab 12 — semantic search** (pgvector + local embedding model + hybrid full-text). Good Aperio story: `db/migrations/013_vec_meta.sql` marks a vector store stale when the embedding model changes and falls back to full-text.
- **Decided 2026-10-17:** API = **Minimal API**, CQRS-lite and FluentValidation added **on the fly** when a lab needs them — see "API shape".

- **Decided 2026-10-18 (Lab 00 scope, option A):** full Lab 00 — both engines at once (Postgres native + SQL Server under Rosetta), `src/Payments.Db` DbUp runner, `develop` branch, glossary. Rejected the Postgres-only slice: Rosetta is the risky part, and it is cheaper to find that out now than in Lab 04 next to procedures and locking.
- **Rule from 2026-10-18: three files per lab.** `README.md` = spec and "Done when". **`WORKBENCH.md`** = the ordered bench sheet the user works through; every step is **Goal / Why (3-4 sentences) / Do / Proof**, and the file ends with a Traps table and a "Notes while you work" section. `QA.md` = interview questions. Steps live in `WORKBENCH.md` only, so the spec stays still while the steps move. Lab 00 is the template.
- **Verified 2026-10-18:** this Mac has `dotnet 10.0.401` (section 9 said `10.0.302`). Docker Desktop was not running at the start of the session.

- **Rule from 2026-10-18: `WORKBENCH.md` is written at the start of its own lab**, never in advance, from the blueprint `labs/_TEMPLATE/WORKBENCH.md`. A workbench written three labs early is wrong by the time you reach it. When a lab teaches a better shape, the blueprint changes in the same commit.
- **Rule from 2026-10-18: Mermaid diagrams everywhere.** The user learns from visuals. Mermaid only (renders on GitHub, diffs as text). Every lab `README.md` has at least one diagram; every `WORKBENCH.md` opens with a **"The target state"** diagram before step 1, and adds one inside any step whose shape is hard to hold in your head. Type per shape and the "label every arrow" rule are in `CLAUDE.md` → Diagrams. Lab 00 is the worked example: target state (`flowchart`), Gitflow (`gitGraph`), one `migrate` run (`sequenceDiagram`), payment life (`stateDiagram-v2`), four-party model (`flowchart`).

- **Rule from 2026-10-18: `CARRY-FORWARD.md` is the inbox.** One-line rows with a `CF-NNN` id and a date: findings, fix-later, open questions, ideas. This section 8 holds **decided** things only. An item graduates from the inbox to here, to a lab `README.md`, or it is dropped — never both places. At the end of each lab, the lab's `WORKBENCH.md` "Notes while you work" moves into the inbox and the inbox gets groomed. Only the user decides a graduation.

- **Rule from 2026-10-18: scaffolds.** When a lab deliverable is a document the user must write in their own words, the agent creates it at the start of the lab as **structure only** — headings, one empty bullet per item, the rule the content must pass, a self-check list — and never fills the content. The file says `Written by: **you**`. First one: `labs/00-setup-and-glossary/GLOSSARY.md` (28 words, empty). Also applies to Lab 10's migration note and Lab 18's design doc.
- **Rule from 2026-10-18: `QA.md` is read twice.** Before a lab, the **questions only** (`grep -E '^\*\*Q' …/QA.md`), 5 minutes, answers covered. After the lab, answer out loud, check, mark the misses, and re-do the marked ones next session. Both are steps in every `WORKBENCH.md` (Step 0 and the step before "Close the lab"). Reading the answers upfront turns recall into recognition, which does not survive an interview.

- **Rule from 2026-10-18: always back-merge `main` into `develop`.** Merging `develop` into `main` leaves a merge commit that lives only on `main`, so `develop` is behind from that moment. Every "Close the lab" step now ends with `git checkout develop && git merge main`, and the proof is `git log --oneline develop..main` being empty. Updated 2026-10-19: the back-merge also runs at the **start** of a lab, in step 1, before the feature branch is cut — otherwise the branch starts from a `develop` that is missing whatever already landed on `main`. Lab work — including the lab's spec, bench sheet and tests — never goes straight to `main`; it goes on `feature/lab-NN-…`. Found when the Lab 01 prep was committed on `main` and `develop` sat five commits behind.
- **Rule from 2026-10-18: [`docs/PROGRESS.md`](../PROGRESS.md) is the status board.** One row per lab with a status (`Done` / `In progress` / `Prepared` / `Spec only`), the tag, and a one-line "what is left", plus a tick list for the lab in flight. The row changes in the same commit as the work. A lab turns `Done` only in its "Close the lab" step. One line only — anything longer is a `CF-NNN` row or a decision here.

- **Rule from 2026-10-18:** `TestResults/`, `*.trx` and `*.coverage` are git-ignored. The test runner writes that folder itself on every `dotnet test` — a `.trx` report and, with `--coverage`, a coverage file. The terminal already shows pass and fail counts, so in these labs the folder is noise. It matters only when a CI server has to publish a report, or when you want a coverage number. Delete it whenever; it comes back on the next run and nothing depends on it.

- **Decided 2026-10-18: xUnit v3** is the unit test framework for every lab. Plain `Assert`, no assertion library. It needs the `test.runner` block in `global.json`, because .NET 10 dropped the old VSTest host and xUnit v3 test projects run on Microsoft.Testing.Platform. Lab 01's 160 tests are written in it. Reqnroll (Lab 07) and Testcontainers (Lab 02) both work with it.
- **Decided 2026-10-18 (docs layout, final):** the root keeps **two** files — `README.md` and `CLAUDE.md`. Everything else is under `docs/`: `docs/PROGRESS.md`, `docs/EVERYDAY-COMMANDS.md`, `docs/plan/` (plan, spec, inbox), `docs/aperio/` (map, tour, talking points, agent-ready notes). Moved with `git mv`, so history follows. The map is a table in `README.md` and in `CLAUDE.md`; a new doc gets a row in both.

- **Decided 2026-10-19 (lab 02 scope):** the full lab — core **plus** both stretches. Core = 7 endpoints, ProblemDetails, Dapper store, Testcontainers tests (steps 1–13). **Amended 2026-10-20:** the branch closed short of this. Shipped: the store with replay, `ProblemMapper`, the contracts, `POST /payments`, `GET /payments/{id}`, and the `HealthTests`, `CreatePaymentTests`, `GetPaymentTests` and `StoreTests` groups. Dropped, each with a row in the inbox: `ValidationTests` (`CF-020`), the five command endpoints and their four test groups (`CF-021`, picked up by lab 03), stretch A paging (`CF-022`), stretch B EF Core and `STORE-NOTES.md` (`CF-023`). The store contract got the full treatment, which is the part labs 03 to 05 lean on. Stretch A = keyset paging with an opaque cursor (14–16). Stretch B = a second `IPaymentStore` on EF Core, both under the same tests (17–19). About 6 hours, not the usual 2–3; the core is a complete lab on its own and step 13 is the marked stopping place.
- **Decided 2026-10-19 (merchant placement):** `MerchantId` lives in the **database and the API only**. The lab 01 aggregate is not reopened for it, because the state machine never reads it. Moving it into the aggregate for later per-merchant rules is `CF-014`.
- **Decided 2026-10-19 (`Payment.Id` is a `Guid`):** `Guid` in the domain, `uuid` in Postgres, a uuid string in JSON. Lab 02 step 2 changes `Payment.Create` and 4 lab 01 test call sites; the blank-id rule becomes "`Guid.Empty` is refused". One type end to end, no parsing, and the narrow `uuid` index that labs 03, 04 and 12 assume. New ids are `Guid.CreateVersion7()`, so inserts land next to each other in the index instead of scattering.
- **Decided 2026-10-19 (how a stored payment is rebuilt): replay.** The store loads the operation rows in order and re-applies them through the real domain methods, with a `ReplayClock : TimeProvider` that hands back the stored timestamps, then checks the replayed status against the stored `status` column and throws on a disagreement. The state machine stays the single source of truth and every read re-proves it; `status` is a verified cache. The costs — N method calls per read, and a row written under an older rule that can refuse to replay — are accepted on purpose and tracked as `CF-015`. The rejected alternative was a `Payment.Rehydrate(...)` factory that sets the fields directly. **Amended 2026-10-20:** `ReplayClock` takes a second `TimeProvider` and falls back to it once the stored stamps run out, instead of throwing. The queue holds exactly `created_at` plus one stamp per operation, so the replay drains it — and the aggregate a command then acts on is the replayed one. Throwing meant every command endpoint died on its first line (`loaded.Payment.Authorise()`), which `StoreTests.Update_inserts_only_the_new_operations` caught. The status check, not the clock, is what catches a corrupt row, and it is the stronger guard.
- **Decided 2026-10-19 (Postgres naming):** `snake_case` tables and columns, unquoted. Postgres folds unquoted identifiers to lower case, so `PascalCase` would need `"quotes"` on every line of SQL forever. SQL Server keeps `PascalCase` (lab 04), so the lockstep rule in `db/README.md` is about columns and types, not about spelling. Dapper bridges the gap with an alias per column in the SQL — `select created_at as "CreatedAt"` — and **not** with the global `DefaultTypeMap.MatchNamesWithUnderscores`, which changes every query in the process from one line far away.
- **Decided 2026-10-19 (error codes): 409 is "not now", 422 is "not like that".** A state conflict (`IllegalTransition`) is `409`; a well-formed request with a wrong number (`AmountExceeds…`, `AmountNotPositive`, `CurrencyMismatch`) is `422`. Shape failures are `400` from .NET 10's built-in validation. Every body is RFC 9457 ProblemDetails with a stable `code` extension and a `urn:payments-labs:error:<code>` type — a URN, because a URI is required but inventing an `https://` domain that serves nothing is worse than saying so. One mapper owns the table.
- **Verified 2026-10-19 (lab 02 tooling, built and run on this Mac):** .NET 10's minimal API validation is `builder.Services.AddValidation()` with DataAnnotations on the request record — no package, and it returns `400 application/problem+json` with an `errors` dictionary. `WebApplicationFactory<Program>` works under xUnit v3 on Microsoft.Testing.Platform, but needs `<InternalsVisibleTo Include="Payments.Api.Tests" />` in the API csproj, because top-level statements make `Program` internal. Package versions that resolve today: Dapper 2.1.89, Npgsql 10.0.3, Testcontainers.PostgreSql 4.16.0, Respawn 7.0.0, Microsoft.AspNetCore.Mvc.Testing 10.0.13, Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3. There is no `xunit3` project template installed, so the test csproj is written by hand from the lab 01 one.
- **Decided 2026-10-20 (`IPaymentStore` method names): `InsertAsync` / `UpdateAsync`, not `InsertAsync` / `SaveAsync`.** `Insert` and `Update` are a matched pair and say which SQL runs: `Insert` writes a new `payment` row, `Update` changes an existing one and appends only the operations from `persistedOperationCount` on. `SaveAsync` next to `InsertAsync` is lopsided and reads like "call me after every operation", which is wrong: a `409` or a `422` returns from the failure branch and writes nothing. EF Core's own `ctx.SaveChangesAsync()` keeps its name inside the stretch B store — that is EF's API, not ours. Lab 02 `WORKBENCH.md`, `TEST-LIST.md` and `README.md` were renamed in the same commit.
- **Decided 2026-10-20 (lab 03 scope):** keys live in their **own table**, `idempotency_key`, primary key `(merchant_id, key)` — not a column on `payment`, because a refund writes no new `payment` row. **Two phases:** phase 1 claims the key with `insert … on conflict do nothing returning` and commits; a request that finds the key `InProgress` gets `409 idempotency_request_in_progress` with `Retry-After: 1`. Phase 2 does the work, and the key's `Completed` mark commits **in the same transaction** as the payment write — the Stripe *atomic phase*. Domain `409`/`422` answers are stored and replayed; a `404` and a `400` are decided before the claim and never stored; an exception releases the key. Lab 03 builds the `authorise`, `capture` and `refund` endpoints it needs; `void` stays in the inbox (`CF-025`). The 8 `ValidationTests` are written here (`CF-020`). **No EF Core** in lab 03: QA Q21–Q25 are answered from `AGENT-READY-BACKEND.md` §5, and EF gets real code in lab 09. **No FluentValidation** yet (see "API shape"). The CI gate (`CF-006`) moves to after lab 03.
- **Decided 2026-10-21 (lab 08 scope):** API keys with scopes **plus** the three tools. The per-call **audit row moves to lab 14**, and keyset paging stays in the inbox (`CF-022`): `list_payments` is a **capped list** — at most 20 rows, `created_at desc, id desc`, and `truncated` — built so a `nextCursor` field and a `cursor` argument can plug in later. **Every `/payments` call needs a key** (`401` without one; `/health` stays open). Keys live in an `api_key` table as a **SHA-256 hash**, with a `kind` (`agent` or `service`) and **one merchant per key**; another merchant's payment is `404`. Scopes: `payments:read`, `payments:write`, `payments:refund`, `payments:approve` — and a `check` constraint stops any agent key from holding `payments:approve`. The scope check is an endpoint filter in the API, never in the MCP server. The confirmation token lives in the **MCP server's memory** (5 min, single use, bound to the parsed arguments) and becomes the refund's `Idempotency-Key` as `mcp-<token>`. Checked the same day on `ModelContextProtocol` 2.3.0: the host logs to stdout by default, the SDK drops unknown arguments silently and does not enforce its own schema's `enum`/`minimum`/`maximum`, and `dotnet run` prints to stdout when a `launchSettings.json` exists — so the lab has an argument fence, string-typed arguments, logs on stderr, and starts the built `.dll`.
- **Decided 2026-10-21 (no cloud models in any lab):** every demo runs locally. Lab 08 is driven by hand in the **MCP Inspector** (no model); lab 09 puts a local gemma on `llama-server` in front of the same tools. Claude Code is no longer a planned MCP client. The reason is the company's, not ours: payment data and what an agent reads must not leave for an external AI provider. Whether the real company is fully local stays a question to **ask** (`CF-001`), not a bet to make in the interview.
- **Decided 2026-10-21 (host ports and project name):** the containers publish on **non-default host ports** — Postgres `55432:5432`, SQL Server `51433:1433` — so a clone never fights a database the machine already runs on 5432 or 1433. Inside the containers the ports stay the defaults. The compose file sets `name: agent-ready-payments`, so Docker Desktop no longer shows the stack under the folder name `docker`. The new name also means new volume names: the old `docker_…` volumes are left behind and the runner re-creates the lab databases. Testcontainers already picks random ports, so the tests do not change.
- **Rule from 2026-10-19: a lab may have more than three files.** The three — `README.md`, `WORKBENCH.md`, `QA.md` — are mandatory. From lab 02 on, a lab also gets a **`TEST-LIST.md`** (the agent writes the test names and the rule each one checks; **you** write every body) and any scaffold the lab needs (lab 01 `TDD-NOTES.md`, lab 02 `STORE-NOTES.md`). Those extra files are listed in the lab's own `README.md`, not in the repo-root table.

- **Decided 2026-10-22 (the PR gate, `CF-006`):** `develop` → `main` goes through a **pull request with a required build-and-test check**. `feature/lab-NN` → `develop` stays a local `--no-ff` merge, and the tag stays manual (`git tag lab-NN`). The PR is the gate, not the review — you are the only reviewer. Gating `develop` as well buys nothing: being briefly broken there is an integration branch's job.
- **Decided 2026-10-22 (how a test says whether CI can run it):** a trait, `[Trait("runs", "local")]`, on the tests that **cannot** run on a GitHub runner. CI excludes them; everything else runs. **The tag marks the exceptions, not the rule** — so a new test runs in CI unless you say otherwise, and a forgotten tag turns CI red loudly instead of skipping a test in silence. Nothing carries the tag today. It goes on lab 04's SQL Server tests (the amd64 image under Rosetta is a Mac trick), lab 09's `llama-server` tests and anything needing the Pi (lab 11).
- **Decided 2026-10-22 (no CI branch):** the gate is built as a **proof of concept on `develop`**, not on a branch of its own. It is one workflow file; a branch, a spec and a bench sheet around it is ceremony. `CLAUDE.md`'s branch list stays as it is.
- **Verified 2026-10-22 (the CI test command):** it is `dotnet test Payments.slnx --filter-not-trait runs=local`, with **no `--` separator**. Measured: 324 tests, 0 failed, 9.5 s with Docker up. Two traps, both measured on this Mac:
  - `dotnet test <project> -- <anything>` reports **"Zero tests ran", exit 5**. The `--` passthrough is broken for this runner; even `-- -noLogo` kills the run. The xUnit flags (`-trait-`) only work through `dotnet run --project <test project> -- -trait- "runs=local"`.
  - A misspelt filter option also gives "Zero tests ran", exit 5. Non-zero, so CI goes red. A typo in the filter can never pass silently.

- **Decided 2026-10-22 (a load is one snapshot):** `DapperPaymentStore.LoadAsync` reads the `payment` row and its `payment_operation` rows inside a **`RepeatableRead`** transaction. Found by the new `ConcurrencyTests`, which no sequential test could have found. `UpdateAsync` writes the `status` column and the new operation rows in one transaction, so a read that straddles that commit sees the **old** status with the **new** operations, replays to `PartiallyRefunded` while the column still says `Captured`, and the replay check throws — a `500` on perfectly correct data. It hits `GET /payments/{id}` too, and hard, because `CommandAsync` loads the payment **before** the claim, so all 50 racers load it, not just the winner. **The isolation level is the whole fix, not the transaction.** Postgres `READ COMMITTED` takes a fresh snapshot for every *statement*, so a plain `BeginTransaction` only makes the bug rarer, which is worse than leaving it. Measured on the full suite: no transaction = 2 failures in 3 runs; `ReadCommitted` = 1 in 3; `RepeatableRead` = 0 in 10. The read is read-only, so it cannot hit a serialisation failure and needs no retry. This is the lab 04 locking lesson arriving early, in Postgres.
- **Decided 2026-10-22 (lab 04 scope):** core **plus one EF Core step** — procs, `UPDLOCK`, `READPAST`, a deadlock made and fixed, RCSI, `rowversion`, then EF Core's `[Timestamp]` and a proc called from EF. The ETag / `If-Match` stretch became its own optional lab, **lab 20** (`labs/20-etag-if-match`): it changes the Postgres API, adds a migration there and touches lab 08's token. The queue is a new SQL Server-only **`PaymentJob`** table. The deadlock lives in **`usp_MarkDuplicatePair`** — two operators flag the same double charge from opposite sides — which adds a nullable `DuplicateOf` column, SQL Server only. The API stays on Postgres; only tests call the procs. `CF-009` and `CF-016` are done **together**, first: `Result` moves to a package-free `Payments.Core`, the migration code to `Payments.Migrations`, and `Migrator` returns a `Result`. The CI gate push (`CF-006`) waits until lab 04 closes.
- **Decided 2026-10-22 (RCSI lives in `Migrator`):** `ALTER DATABASE … SET READ_COMMITTED_SNAPSHOT ON` cannot run inside a transaction (`Msg 226`, measured), and DbUp wraps every script in one. So `Migrator` switches it on, on `master`, `WITH ROLLBACK IMMEDIATE`, right after it makes the database. It is a database option, not schema.
- **Decided 2026-10-22 (a lab may rewrite its own migration while in flight):** still one migration per lab per engine. Until the lab is tagged, its file may be rewritten: drop the dev database and migrate again. Tests make a fresh database anyway. **After the tag the file is frozen**, and a change is a new numbered script. Lab 04's file is `db/sqlserver/0002_payments_procs_locking.sql` (renumbered from `0001` on 2026-10-22: lab 09 lands first and takes `0001`).
- **Decided 2026-10-22 (`CF-037`, how the build gets to zero warnings):** the four `CS8524` switches get a `_ => throw new ArgumentOutOfRangeException(...)` arm, **not** the one-table refactor the row also offered. The arm is one line per switch and changes no behaviour; folding `ProblemMapper`'s `Code`, `Status` and `Title` into one table is a real refactor of a lab 02 deliverable and belongs in a lab that needs it. `CS8321` was closed by **wiring `CheckArgs` up**, not by deleting it: it was written in lab 00 and left commented out, so the migration runner validated nothing and simply indexed `args[2]`. Wiring it as written would have thrown, because the branch its own `args.Length < 3` test falls into then printed `args[0]` and `args[1]`; the message now uses `string.Join`. Bad arguments print a line and exit `2`.

- **Decided 2026-10-22 (lab 09 scope):** the **full lab with one stop mark** after step 15, about 11 hours, and lab 09 runs **before** lab 04. Its parts, each decided with the user the same day:
  - `AgentRun` / `AgentStep` live on **SQL Server** `PaymentsLabs`, owned by EF Core, shipped as `db/sqlserver/0001_agent_run.sql` (`CF-028`). The reason is the repo's goal: prove as many production environments as we can. Those tests carry `[Trait("runs", "local")]`. Lab 04's script becomes `0002`.
  - Lab 09 lifts **`CF-016`** into `src/Payments.Migrations`; `CF-009` stays in lab 04. Lab 09 builds **`CF-033`** as `tests/Payments.Testing` (in-memory MCP server, `StubApi`, `ApiFixture`, `SqlServerDatabase`). Verified 2026-10-22: `ApiFixture` in a class library runs all API tests with no `Mvc.Testing` line in the test project.
  - **The model never sees the confirmation token.** `McpToolHost` cuts it out and keeps it in `AgentRun.PendingCall`; the host confirms after an in-process `IApprovalChannel` yes. A token written by the model is a `Rejected` step. A wait resumed after a restart proposes again and asks again; an approval saved with no confirm after it is `refund_in_doubt` and never asks again.
  - The refund **ceiling** is a host rule, `AgentOptions.RefundCeiling` = 500.00. The turn deadline (`CF-007`) is in this lab.
  - **A live demo is a deliverable**, not only tests: two configured agents (`support-bot`, `read-bot`) in `agents.json`, a `chat` mode, `seed-demo`, `show`, and `labs/09-agent-loop/DEMO.md` with the MCP Inspector scene first. Rehearsed on the picked model.
- **Found and decided 2026-10-22 (the double refund):** two refunds with **different** `Idempotency-Key`s on one payment both won, 29 of 30 rounds, because `DapperPaymentStore.UpdateAsync` wrote `where id = @Id` and nothing else. Lab 03 only raced **one** key. Fixed **inside lab 09** (steps 4–5), not as a hotfix: `UpdateAsync` takes `select … for update`, re-checks the stored operation count against the loaded one and throws `ConcurrentUpdateException` on a mismatch; `CommandAsync` reloads and runs the command **once more**, so the loser gets the domain's answer (`422 amount_exceeds_captured`, or `409 illegal_transition` after a full refund). Rejected: a `version` column with `409 concurrent_update` — it takes lab 20's column early, and an agent cannot act on "conflict". Measured in a scratch copy: 0 doubles in 90 rounds, the 114 API tests green.

- **Decided 2026-10-23 (engine parity is its own lab, lab 21):** the API gets to run on **either** engine, picked by one configuration line (`Database:Engine`), and that work is **`labs/21-engine-parity`**, placed **after lab 06**. The seams already exist — `IPaymentStore`, `IIdempotencyStore`, `IPaymentQueries`, `IApiKeyStore` — so the lab adds a second implementation behind them, the two SQL Server tables lab 03 and lab 08 never got (`IdempotencyKey`, `ApiKey`), the startup switch, and the API suite as a `[Theory]` on the engine. It runs **after** labs 04–06 on purpose: by then the T-SQL has been written by hand, so the lab is typing, not learning. **Lab 04 does not grow** — it stays procs and locking. Rejected: doing parity first, which parks lab 09 and lab 04 for a week. Measured the same day: the whole Postgres-vs-SQL-Server difference across today's 465 lines of store code is **six** places (`on conflict … returning`, `now()`, `::text` on a null parameter, `limit` vs `offset/fetch`, the column alias, the three type names) plus `for update` → `with (updlock, rowlock)` and the isolation question. **Correction to the 2026-10-22 `runs=local` reasoning:** that tag is about *this Mac* needing Rosetta, not about CI being unable — `mcr.microsoft.com/mssql/server:2022` is **native** on an `ubuntu-latest` runner, so lab 21 runs the SQL Server half in CI as a service container and the parity claim becomes a required check. Open: which engine becomes the default afterwards (`CF-039`).

- **Decided 2026-10-23 (the lockstep rule covers every shared table, and twins land in the same commit):** `db/README.md` named only `Merchant`, `Payment` and `PaymentOperation` as the shared set, so lab 03's `idempotency_key` and lab 08's `api_key` were written for Postgres only and the two engines drifted. From today the shared set is **every table the API reads or writes**, and **a new Postgres migration gets its SQL Server twin in the same commit** (and the other way round). Lockstep is still about columns, types and rules, not spelling: `snake_case` in Postgres, `PascalCase` in SQL Server (2026-10-19). Engine-only columns stay allowed and must say so in the script — `Payment.RowVer` and `Payment.DuplicateOf` are SQL Server only (lab 04), `Policy.Embedding` is Postgres only (lab 12). Lab 21 pays off the drift that already happened; this rule stops any new drift from today. Written into `CLAUDE.md` so it cannot be forgotten.

- **Decided 2026-10-23 (flowcharts go down, and no `;` in a diagram line):** every `flowchart` in the repo is `TD`. GitHub renders a diagram into a fixed page width, so `LR` squeezes a long chain into a thin strip and the box text turns unreadable — found by reading lab 09's bench sheet on the web. All 19 `flowchart LR` blocks were flipped the same day, plus the row in `CLAUDE.md`'s diagram table and `labs/_TEMPLATE/WORKBENCH.md`. `LR` is allowed only for a chain of three boxes or fewer. The second half of the rule was found while fixing the first: **Mermaid treats `;` as the end of a statement**, so `A->>P: begin; select 1 … for update` parses as a line with no arrow and GitHub prints a parse error instead of the picture. That also caught a diagram broken since lab 00 — `participant FS as db/&lt;engine&gt;/*.sql`, where the `;` of each HTML entity cut the line in half; it is now `db/[engine]/*.sql`. Measured the same day: all **50** Mermaid blocks in the repo parse with Mermaid 11. The checker is a scratchpad script for now; making it permanent is `CF-040`.

## 9. Environment checklist

- `dotnet --version` → `10.0.401` ✓ (pinned by `global.json`, `rollForward: latestFeature`)
- Start Docker Desktop. Turn on **"Use Rosetta for x86_64/amd64 emulation"** in Settings → General.
- SQL Server image: `mcr.microsoft.com/mssql/server:2022-latest` with `platform: linux/amd64`.
- Postgres image: `pgvector/pgvector:pg17` (native arm64; Postgres 17 with pgvector built in, needed for Lab 12).
- Local model (Lab 09): `./scripts/llm.sh`. The model and the context live in `.env` (`LLM_HF_MODEL`, `LLM_CTX=65536`), so rung 1 and rung 3 are scored at the same context. Measured 2026-10-23 on the M1 Pro: rung 1 at `-c 16384` uses 5.49 GB and at `-c 98304` uses 6.75 GB. Check that it works: `curl http://localhost:8080/v1/models`.
