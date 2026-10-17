# Lab 17 — Interview Q&A: cold start

---

## A. The idea

**Q1. What is a cold start?**
A: The extra time the first request pays because nothing is ready yet: code not compiled, connections not open, caches empty, models not loaded. Every later request is "warm".

**Q2. Name the cold-start layers in an agent stack.**
A: The .NET process (JIT, DI), the database pool (TCP, TLS, login), the ORM (EF model, query translation), the MCP server process, model weights, the KV cache, the agent's own context, and data itself (a new merchant has no history).

**Q3. Which layer is usually the biggest?**
A: Loading the model weights: tens of seconds, or minutes if the weights must be downloaded. Next comes prefill of the system prompt and tool schemas with an empty KV cache. .NET and DB cold starts are usually around a second.

## B. Live vs ready

**Q4. What is the difference between liveness and readiness?**
A: **Live** means the process is running and not stuck. If it fails, restart the process. **Ready** means it can serve real traffic now: warm-up done, dependencies reachable, model loaded. If it fails, send no traffic yet, but don't restart.

**Q5. Give a real example where "healthy" was not "ready".**
A: llama-server's router answers `/health` in about 0.5 s on a cold start, before any model is loaded, because models load lazily on the first chat request. Aperio documents this (`lib/agent/providers/llamacpp.js` ~118) and checks the real model status with `isModelLoaded()` (`lib/helpers/modelProgress.js`). If you route traffic on `/health` alone, the first users pay the full load.

**Q6. What is a Kubernetes `startupProbe` for?**
A: It gives a slow starter (model loading, big warm-up) time to finish before the liveness probe can kill it. Without it, a pod that needs 60 s to load can be restarted in a loop, and never become ready.

## C. Fixing it

**Q7. How do you warm up a .NET service?**
A: Use an `IHostedService` that runs before readiness turns true. It opens the DB pool with a cheap query (`SELECT 1`), touches the EF Core model or uses a compiled model, calls each critical dependency once, and for agents loads the model and sends one hidden request to fill the KV prefix. Readiness turns true only when it finishes.

**Q8. ReadyToRun vs Native AOT?**
A:
- **ReadyToRun:** ships pre-compiled code next to IL. Startup is faster, the JIT still runs for hot paths, and nearly everything stays compatible.
- **Native AOT:** compiles the whole app to native code. Startup is fastest and memory is smallest, but there is no runtime code generation. Reflection-heavy libraries break. Minimal APIs and `System.Text.Json` source generators support it, EF Core needs care, and Dapper needs `Dapper.AOT`.

**Q9. What is tiered compilation, in one line?**
A: The JIT first makes fast-to-compile code, then recompiles hot methods with full optimisation (dynamic PGO uses runtime data to do it better). It is good for steady state, but the first calls still run slower code.

**Q10. How do you warm the database side?**
A: Open the pool early with a few connections (`Min Pool Size` in the SqlClient connection string), run one cheap query, and keep plans warm for hot stored procedures. After a SQL Server restart, the plan cache is empty too, so the first proc calls compile.

**Q11. How did Aperio fix the model cold start?**
A: It moved the cost from the first user message to server boot. `preloadMainModel()` fires a warm-up request right after llama-server is up, which loads the model and fills the KV cache with the real system prompt. Meanwhile a status bus shows "downloading X — N of M GB" to every open client (`lib/helpers/modelPreload.js`).

**Q12. Why show "starting up" instead of just waiting?**
A: A silent wait looks like a hang. Users click again, and clients time out and retry, which adds load to a system that is already busy starting. An honest status stops the retries and sets expectations. Aperio replays the latest status to any client that connects mid-load, so nobody sees a chat that only looks ready.

## D. Scale and agents

**Q13. What is a thundering herd after a deploy?**
A: Many pods or clients start at once and all hit the same cold dependency together: DB logins, model loads, cache misses. Fix it with rolling deploys a few pods at a time, jittered retries, a circuit breaker, warm-up before ready, and a single-flight lock, so one warm-up runs and the other callers wait for it.

**Q14. What is "single-flight"?**
A: When many callers need the same expensive thing at the same time (loading the model, building a cache), only the first one does the work, and the rest wait for its result. In .NET: a `Lazy<Task<T>>` or a `SemaphoreSlim` around the warm-up.

**Q15. Should each conversation spawn its own MCP server process?**
A: Usually not at scale. Spawning costs process start and `tools/list` on every conversation, and duplicates background work. Aperio spawns one MCP process per agent session, and its code skips a background reindex in each child because several can be alive at once (`mcp/index.js`). For `Payments.Mcp` I keep one long-lived process per agent host.

**Q16. What is an agent's "context cold start"?**
A: A new session starts with an empty context window. Load too little, and the first answer is wrong or needs extra tool calls. Load too much, and it is slow and expensive. Load a bounded start: the resume ledger if a run is resuming, plus a few relevant facts. Aperio loads session memory at session start (`lib/agent/memory-context.js`).

**Q17. What is a "data cold start"?**
A: A new merchant, product or model has no history, so similarity search and risk scores have nothing to compare with. Use clear rule-based defaults and flag the case for a human until there is data. Aperio lists a related open item for document templates with no history yet (`id/reference/tech-debt.md`, "cold-start template proposals").

**Q18. Serverless cold start — how is it different?**
A: Functions (for example Azure Functions on a consumption plan) scale to zero, so a request after a quiet period pays process start plus warm-up. Fixes: a premium plan with pre-warmed instances, keeping the package small, and AOT. That is why a model-serving or payment-critical path rarely runs on scale-to-zero.

**Q19. How do you prove you fixed it?**
A: A startup timeline with spans per layer, cold vs warm, before and after. Plus a test: restart, fire 10 parallel first requests, and check that there is one warm-up, no timeouts, and that readiness stayed false until the work was done.

## Aperio: have you solved this before?

**Q-APERIO. "Have you solved this before?"**
A: Yes, the hardest part. In Aperio the first user message used to pay minutes of model download and load, because llama-server loads models lazily. I moved that to boot with a preload and a KV-cache warm-up, added a status bus so users see "downloading N of M GB" instead of a chat that only looks ready, and I know llama-server's `/health` turns green before the model is loaded. What Aperio doesn't have is the .NET side: JIT vs ReadyToRun vs AOT, DB pool warm-up and readiness gating. This lab adds those.
Details and file references: the "Aperio comparison" table in this lab's `README.md`.
