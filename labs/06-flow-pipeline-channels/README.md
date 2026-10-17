# Lab 06 — Flow-based processing (settlement pipeline)

## What we solve
Process a big batch with many workers in parallel, without flooding memory, and stop cleanly.

## Simple example
End of day: 200,000 captured payments must be settled (fees computed, totals per merchant written).
- A **producer** reads payments page by page.
- **4 workers** compute fees at the same time.
- **1 writer** saves results in batches of 1,000.

If the writer is slow, the queue fills up and the producer **waits** (backpressure) instead of eating all memory. One payment has broken data — it goes to a **poison** list; the other 199,999 still settle. Someone stops the service mid-run — workers finish what they hold, nothing is half-written.

## Why it matters for a payment platform
The ad asks for *"flow-based processing"* and *"highly concurrent and multithreaded"* backends. This is both, with `System.Threading.Channels`.

## Everyday picture
A **sushi conveyor belt.** The chef (producer) puts plates on the belt. Four guests (workers) take plates. One waiter (writer) collects empty plates in stacks of ten. When the belt is full, the chef **stops and waits** — that is backpressure. A bad fish goes in the bin with a note (poison), not back on the belt. At closing time, the chef stops first, guests finish their plates, the waiter takes the last stack.

## EF Core angle
One rule: never share one `DbContext` between workers. Use `IDbContextFactory<T>` and one context per unit of work. `DbContext` is not thread-safe.

## Aperio comparison (our backbone)
| Case | Aperio | Where (verified 2026-10-17) | What this lab adds |
|---|---|---|---|
| Background queue with retry + backoff | ✅ Has | `lib/helpers/embedding-queue.js`: items keyed by id (no duplicates), 3 attempts, backoff 30 s → 60 s, flush every 60 s | — |
| One queue per destination | ✅ Has | separate self-memory queue after bug F-R2-06 (`mcp/index.js` 79–89) | One `Channel<T>` per destination |
| Bounded queue / backpressure | ❌ No | the queue is an unbounded `Map`; items waiting on a reindex are dropped on purpose, because an unbounded wait is "the worse failure" (comment in the file) | `BoundedChannel` with `Wait` |
| Poison / dead-letter store | ⚠️ Partly | dropped after 3 failures with a log warning; no dead-letter table | Poison channel + table |
| Parallel workers | ❌ No | one sequential flush loop (Node event loop) | N workers, one batching writer |
| Serialised work per session | ✅ Has | `lib/emitters/handlers/ws/turnLock.js` | `SemaphoreSlim` |

## Done when
Tests prove backpressure, cancellation, poison handling and graceful shutdown.

Spec + failing tests: written when the lab starts.
