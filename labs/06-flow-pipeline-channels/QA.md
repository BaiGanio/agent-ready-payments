# Lab 06 — Interview Q&A: flow-based processing, Channels, concurrency

The ad asks for *"flow-based processing"* and *"highly concurrent and multithreaded"* systems. Here both meet: a settlement pipeline.

---

## A. Flow-based processing

**Q1. What is flow-based processing?**
A: The work is a graph of small independent steps connected by queues. Each step reads input, does one thing, writes output. Steps run concurrently. Data flows; components don't call each other directly.

**Q2. Describe your settlement pipeline.**
A: Producer reads captured payments in pages → bounded channel → N workers compute fees and net amounts → bounded channel → one writer batches results and saves them (bulk copy, Lab 05). Failures after K retries go to a poison channel and a dead-letter table.

**Q3. Why one writer?**
A: Batching writes is far faster than one insert per item, and one writer avoids contention and deadlocks on the target table. Workers are CPU/IO parallel; the writer is the sink.

**Q4. Channels vs TPL Dataflow vs `BlockingCollection`?**
A: `BlockingCollection` blocks threads (sync). **TPL Dataflow** has rich blocks (`TransformBlock`, `BatchBlock`, linking, bounded capacity) — good for bigger graphs. **`System.Threading.Channels`** is a lean async producer/consumer primitive; you compose the graph yourself. I chose Channels for control and clarity.

## B. Backpressure and bounds

**Q5. What is backpressure?**
A: When a consumer is slow, the producer slows down instead of filling memory. With `Channel.CreateBounded(capacity)` and `FullMode = Wait`, `WriteAsync` waits until there is space.

**Q6. Bounded vs unbounded channels?**
A: Unbounded never blocks the writer — and can eat all memory if the consumer is slower. In production pipelines: bounded, always.

**Q7. Other `BoundedChannelFullMode` options?**
A: `DropOldest`, `DropNewest`, `DropWrite`. Fine for telemetry; never for payments — every item must be processed.

**Q8. How did you prove backpressure works?**
A: A test with a slow fake writer and a counting producer: the producer's count never gets ahead of consumed + capacity.

## C. Cancellation and shutdown

**Q9. How does cancellation flow?**
A: One `CancellationToken` from the host (`BackgroundService.ExecuteAsync(stoppingToken)`) passed to every `ReadAsync`/`WriteAsync`/DB call. Cancel → everyone stops waiting.

**Q10. Graceful shutdown — the order?**
A: Stop the producer → `writer.Complete()` on its channel → workers drain the remaining items and then complete their output channel → writer flushes the last batch → done. `await reader.Completion` at each stage. Hard-cancel only after a timeout.
Trap: "What if you cancel instead of completing?" → In-flight items are lost or half-done. Completion drains; cancellation aborts.

**Q11. How do you read until the channel is done?**
A: `await foreach (var item in reader.ReadAllAsync(ct))` — ends when the channel is completed and empty.

## D. Failures

**Q12. Retry policy?**
A: Retry transient errors only, with exponential backoff + jitter, max K attempts. Business errors (bad data) go straight to poison — retrying won't fix them.

**Q13. What is a poison message?**
A: An item that fails every time. Without a poison queue it blocks or crashes the pipeline forever. Move it aside with the error, keep going, alert.

**Q14. What if the process dies mid-batch?**
A: The source must be re-readable: items are claimed in the DB (status `Settling`, `READPAST` claim from Lab 04) and only marked `Settled` when the writer commits. On restart, stale claims are released. Processing is idempotent, so a repeat is harmless.

## E. Threads, async, concurrency primitives

**Q15. How many workers?**
A: CPU-bound: about `Environment.ProcessorCount`. IO-bound: more, limited by what the DB can take. Make it a setting and measure.

**Q16. `Task.Run` vs async I/O?**
A: `Task.Run` pushes CPU work to a pool thread. Async I/O uses no thread while waiting. Don't wrap I/O in `Task.Run`.

**Q17. `SemaphoreSlim` — when?**
A: To cap concurrency on a resource (e.g. max 10 parallel calls to an acquirer). `await sem.WaitAsync(ct)` + `try/finally Release()`. Aperio equivalent: the per-session turn lock serialises turns (`lib/emitters/handlers/ws/turnLock.js`).

**Q18. `lock` vs `Interlocked` vs `ConcurrentDictionary`?**
A: `lock` for short critical sections with no `await` inside (you can't `await` in a `lock`). `Interlocked` for single atomic counters. `ConcurrentDictionary` for shared maps — careful, `GetOrAdd`'s factory can run more than once.

**Q19. What is thread-pool starvation?**
A: Blocking calls (`.Result`, `.Wait()`, `Thread.Sleep`) hold pool threads; new work waits; the pool grows slowly (about one thread per second or so past the minimum). Symptom: latency spikes with low CPU. Fix: async all the way.

**Q20. `Parallel.ForEachAsync` vs a channel pipeline?**
A: `Parallel.ForEachAsync` is great for "process this list with N in parallel". A pipeline is better when stages have different speeds, you need batching at the end, backpressure between stages, or continuous input.

**Q21. `ConfigureAwait(false)`?**
A: In library code, it skips resuming on the captured sync context. Matters in .NET Framework ASP.NET and UI apps (deadlock risk with `.Result`). ASP.NET Core has no sync context, but library code should still use it.

**Q22. How does this relate to Aperio?**
A: Aperio's embedding queue is the same producer/consumer idea in Node. And its bug F-R2-06 is a good story: two logical stores shared one queue, so self-memory items were gated and written against the wrong table. Fix: a separate queue per store (`mcp/index.js` ~79–89). In C# that is "one `Channel<T>` per destination, never a shared one with a type switch".

**Q23. Can workers share one EF Core `DbContext`?**
A: No. `DbContext` is not thread-safe; two concurrent operations on one context throw. Use `IDbContextFactory<T>` and create one short-lived context per unit of work. Same for a `SqlConnection`: one per worker, from the pool.


## Aperio: have you solved this before?

**Q-APERIO. "Have you solved this before?"**
A: Partly. Aperio's embedding queue retries with exponential backoff, drops duplicates by id, and after a real bug (F-R2-06) uses one queue per destination. But it is unbounded, has no dead-letter table and runs one item at a time on Node's event loop. This lab adds what it lacks: bounded channels with backpressure, parallel workers, a poison queue and graceful shutdown on real threads.
Details and file references: the "Aperio comparison" table in this lab's `README.md`.
