# Lab 17 — Cold start, at every layer

## What we solve
The **first** request after a deploy, a restart or a quiet night is much slower than every request after it, and sometimes it fails. In an agent stack, every layer adds its own cold start. This lab measures each one and removes it, or makes it honest.

## Simple example
Monday 07:00. A new version is deployed. The first support person asks the agent: *"Did order 1234 get charged twice?"*

| Layer | Cold | Warm |
|---|---|---|
| `Payments.Api` .NET process: JIT, DI container, routing | ~1 s | ~5 ms |
| DB connection pool empty: TCP + TLS + login to SQL Server / Postgres | ~0.3 s | ~0 |
| EF Core builds its model; first query is translated | ~0.5 s | ~1 ms |
| `Payments.Mcp` process spawned + `tools/list` | ~1 s | ~0 (reused) |
| `llama-server`: model weights not loaded (GBs from disk, or a download) | **20–60 s, or minutes** | ~0 |
| KV cache empty: system prompt + tool schemas processed from zero | ~5 s | ~0.3 s |
| Agent context empty: no task state, no relevant memory loaded | wrong or slow first answer | right answer |
| **Total for the first answer** | **~1 minute** | **~2 s** |

It gets worse:
- The client times out after 30 s and **retries**, so now two cold requests compete.
- Kubernetes saw `/health` green, because the process is up, and sent traffic **before the model was loaded**.
- Ten pods restart at once after the deploy, and they all hit the database login and the model at the same moment: a **thundering herd**.

## Everyday picture
**A restaurant at opening time.** The doors open at 8:00, but the oven is cold, the coffee machine is still heating, the chef is still reading today's orders, and the fridge is half stocked. The first customer waits 20 minutes. A good restaurant **pre-heats before opening**, and keeps the "OPEN" sign off until the kitchen is actually ready. It does *not* turn the sign on as soon as the door is unlocked.

## The shape (kept small)
1. **Measure first.** Write a startup timeline with OpenTelemetry spans (Lab 14): process start → DI ready → DB pool warm → EF model built → MCP tools listed → model loaded → KV warmed → ready. Record cold vs warm for one end-to-end agent request.
2. **.NET process:** compare normal JIT, **ReadyToRun**, and **Native AOT** (Minimal API supports AOT; Dapper via `Dapper.AOT`; EF Core with a compiled model). Note what AOT forbids, such as reflection-heavy libraries.
3. **Warm-up `IHostedService`:** open the DB pool with a cheap query, build the EF model, call `tools/list` once, and send one hidden request to warm the model and its KV prefix (Lab 15).
4. **Health ≠ ready:**
   - `/health/live` = the process is up.
   - `/health/ready` = the warm-up finished **and** the model is loaded (check the model server, not just its `/health`).
   - In Kubernetes, add a `startupProbe` so a slow first load is not killed as "dead".
5. **Honest status while cold:** while the model is loading, the agent answers *"The assistant is starting up (loading model, 40%)"*. It does not look ready, and it does not time out silently.
6. **Long-lived pieces:** keep one `Payments.Mcp` process per agent host and reuse it. Don't spawn one per conversation.
7. **Agent context cold start:** a new session loads a **bounded** starting context: the pinned task ledger (Lab 15) if a run is resuming, plus a few relevant facts. It does not load "everything".
8. **Data cold start:** a brand-new merchant has no history, so semantic search (Lab 12) and any risk signal have nothing to compare with. Use clear rule-based defaults until there is data.
9. **No retry storm:** clients use a longer timeout for the first call, retries with jitter, and a circuit breaker (Lab 16). Pods roll out a few at a time.

## Aperio comparison (our backbone)
| Case | Aperio | Where (verified 2026-10-17) | What this lab adds |
|---|---|---|---|
| Model loaded on the first user message | ✅ Solved | `lib/helpers/modelPreload.js`: llama-server's router loads a model lazily, so "the user's FIRST MESSAGE used to pay the whole download+load (minutes of it)". The cost moved to boot with `preloadMainModel()` | Same for `Payments.Agent`, plus a readiness gate |
| Health green before the model is ready | ✅ Known and handled | `lib/agent/providers/llamacpp.js` ~118: the router "answers /health almost instantly even before a model is loaded … cold start → green in 0.5s"; `isModelLoaded()` checks the real model state (`lib/helpers/modelProgress.js` 114) | `/health/ready` that checks the model, plus a k8s `startupProbe` |
| Honest status while loading | ✅ Has | a process-wide `model_status` bus; a new connection gets the latest status replayed, so the user sees "downloading X — N of M GB" and not a chat that looks ready (`modelPreload.js` header) | The same message from the agent |
| KV prefix warmed before first use | ✅ Has | `warmCache()` (`lib/agent/index.js` ~1074), `warmLlamaCppCache()` (`llamacpp.js` 205); static greeting costs no model time | — |
| Long-lived vs per-session processes | ⚠️ Partly | MCP processes are spawned per agent session; `createContext()` skips a background reindex in each child because several can be alive at once (`mcp/index.js` ~50–60) | One long-lived `Payments.Mcp` per host |
| Session context at start | ✅ Has | session memory loaded at session start (`lib/agent/memory-context.js`, `refreshSessionMemCtx`) | A bounded start context + resume ledger |
| Data cold start | ⚠️ Partly | tech-debt entry "Document Intelligence — cold-start template proposals" (`id/reference/tech-debt.md` ~396): heuristics for when there is no history yet still need real-data validation | Rule-based defaults for a new merchant |
| .NET JIT, ReadyToRun, Native AOT, DB pool warm-up | ❌ No | Node.js | All of it |
| Startup timeline with numbers | ❌ No | — | OpenTelemetry startup spans, cold vs warm table |

## Why it matters for a payment platform
A payments platform deploys often, scales pods up at peak, and restarts after failures. Every cold start is a burst of slow or failed payments, and with agents it is a minute of a "frozen" assistant. Cold-start problems often appear first as "random timeouts after deploy". Knowing the layers, and how to tell live from ready, is a senior answer.

## Done when
- A startup timeline shows each layer's cold time, for both the cold and the warm path.
- `/health/ready` stays false until the DB pool, MCP tools, model load and KV warm-up are done. A test proves it.
- The first agent request after a restart answers in near-warm time, or returns an honest "starting up" status. It never fails silently.
- A table compares JIT vs ReadyToRun vs Native AOT startup time and memory for `Payments.Api`, with one sentence on what AOT cost you.
- A test with 10 parallel first requests shows no retry storm: one warm-up, the others wait or get "starting up".

Spec + failing tests: written when the lab starts.
