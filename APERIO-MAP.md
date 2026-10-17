# Aperio Map — what our backbone has, per lab

Aperio (`github.com/BaiGanio/aperio`) is the backbone of this prep: every lab is compared with it. Facts were checked against the code on **2026-10-17** (commit `e44648d1`). Details and file references are in each lab's `README.md` → **"Aperio comparison"**, and each `QA.md` ends with **Q-APERIO "Have you solved this before?"**.

Legend: ✅ has it · ⚠️ partly · ❌ no

| Lab | Topic | Aperio | Best Aperio evidence | What the lab adds |
|---|---|---|---|---|
| 00 | Setup, words, Gitflow, DB | ⚠️ | Containers, 14 lockstep migrations × 2 engines, SemVer + changelog + release workflow; `normalizeAmount()` refuses to guess separators | Gitflow, SQL Server, DbUp, payments words |
| 01 | Payment state machine | ⚠️ | `agent_interrupts` status `CHECK` (9 values), `InterruptConflictError`, injected clock | Money rules, Result type, strict TDD |
| 02 | Minimal API + Dapper | ⚠️ | Zod tool schemas; real status codes; two engines behind one store | ProblemDetails with stable `code`; Dapper/EF in C# |
| 03 | Idempotency | ⚠️ | In-turn dedup; exactly-once claim; **`approvePending()` race fixed** with `FOR UPDATE` + conditional `UPDATE` | Client `Idempotency-Key` + unique index, 50-parallel test |
| 04 | SQL Server procs + locking | ⚠️ | Row lock (`FOR UPDATE`) and compare-and-swap claim on Postgres | T-SQL, lock hints, `READPAST`, deadlocks, RCSI, `rowversion` |
| 05 | Bare ADO.NET | ⚠️ | Raw drivers, hand-written SQL, `bulkInsert()` | `SqlBulkCopy`, streaming reads, measured numbers |
| 06 | Flow pipeline (Channels) | ⚠️ | Embedding queue: retry + backoff, dedup by id; one queue per destination (bug F-R2-06); turn lock | Bounded channels, backpressure, parallel workers, poison queue |
| 07 | BDD (Reqnroll) | ⚠️ | 14 JSON harness scenarios = Given/When/Then in JSON; e2e + browser tests | Gherkin feature files |
| 08 | C# MCP server | ✅ | MCP server with typed tools; **two-phase confirm + digest + atomic claim**; refuse-don't-repair; shell off by default; fencing + taint | C#; API-only boundary; per-agent scopes **enforced**; audit row per call |
| 09 | Agent loop (local model) | ✅ | Loop across 6 providers; step cap 6, repeat limit 3, failure budget 3; cancel latch; weak-model guards | C#; step-level resume; per-turn wall clock |
| 10 | Framework → Core | ❌ (method ⚠️) | Step-by-step extraction of `turnLock.js` from `wsHandler.js`; coupling map in `AGENTS.md` | The .NET migration itself |
| 11 | k3s on the Pi | ✅ | `k8s/` StatefulSet + Deployment + probes + CD workflow | .NET image for arm64 |
| 12 | Semantic search | ✅ | pgvector; background embeddings; **stale-vector handling** (`013_vec_meta.sql`); full-text fallback | Hybrid RRF, recall test set, injection test, C# |
| 13 | Chargeback workflow | ⚠️ | Durable interrupts with expiry + claim; non-overlapping scheduler; plan checks | Saga with deadline + compensation; workflow vs agent |
| 14 | Observability + audit | ⚠️ | Lifecycle traces, safety events, `agent_runs`, redaction | OpenTelemetry end to end; per-call audit table |
| 15 | Context + caching | ✅ | **KV-cache break measured** (12,577 / 31,067 tokens, 262 s); warm-up; staged trimming with 80k cap; offload; dedup | Read-tool TTL cache with invalidation; pinned task ledger |
| 16 | Failure + recovery | ⚠️ | Per-request timeout, health retry, abort handling; harness `false-write-claim`, `plan-drift` | Retry/backoff + circuit breaker; write-ahead in-doubt steps; task contracts |
| 17 | Cold start | ✅ (model) ⚠️ (rest) | **First message paid minutes of model load → moved to boot** (`modelPreload.js`); `/health` green before the model loads, so check `isModelLoaded()`; honest status bus; KV warm-up | .NET JIT/R2R/AOT, DB pool warm-up, readiness gating, `startupProbe`, single-flight |
| 18 | System design capstone | ⚠️ | Written architecture; coupling map with blast radius; six providers behind one orchestrator; redaction relay; model capacity data (`011_model_facts.sql`) | Multi-node scale, outbox + bus, payment core, LLM gateway — on paper |

## How to use this in the interview

1. **Lead with ✅ rows.** These are "I built it" answers: Labs 08, 09, 11, 12, 15, and the model side of 17.
2. **For ⚠️ rows,** say what Aperio has, then the gap, then the lab: *"In Aperio I did X; it doesn't do Y; I built Y in my labs repo."*
3. **For ❌ rows,** say it plainly, then go straight to the lab. Never stretch Aperio to cover something it doesn't do. A wrong claim is worse than a gap.

## Gaps Aperio states openly (own them first)

- No C#, no SQL Server, no stored procedures.
- Single process (Node event loop); no distributed locks; no load numbers.
- No Gherkin. No OpenTelemetry. No per-call audit table.
- No per-turn wall clock (the step cap is the hard bound).
- Agent filesystem/memory scoping is not enforced at execution time (`id/reference/tech-debt.md`, line 195).
- No general retry with backoff for model calls; no write-ahead "in doubt" step state.
- The embedding queue is unbounded and has no dead-letter table.
- HTTP errors are `{ error }` bodies, not ProblemDetails.
