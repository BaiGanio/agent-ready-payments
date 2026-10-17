# Lab 15 — Interview Q&A: KV cache, context, trimming, tool caching

---

## A. KV cache and prompt caching

**Q1. What is the KV cache?**
A: When a transformer reads a prompt, it computes "key" and "value" tensors for every token in every layer. The KV cache stores them. The next request that starts with the **same tokens** can skip that work and only process the new part. That is the difference between 1 second and 20 seconds per step.

**Q2. Prefill vs decode?**
A: **Prefill** processes the input prompt. It is heavy, but it can run in parallel, and it is what the cache saves. **Decode** generates output one token at a time. Long agent histories make prefill the main cost, so caching the prefix matters most.

**Q3. What breaks the cache?**
A: Any change near the start of the prompt. Everything after the first changed token must be processed again. Common causes:
- a timestamp or request id in the system prompt
- tool schemas in a different order, or one tool added or removed mid-conversation
- a different JSON serialisation (key order, whitespace)
- editing an old message instead of appending a new one

**Q4. How do you design a cache-friendly prompt?**
A: Stable parts first, changing parts last: system prompt → tool schemas (sorted, the same set for the whole session) → history (only appended to) → newest message. Dynamic facts like "today's date" go in the latest message, not in the system prompt.

**Q5. How did you measure the cache?**
A: `llama-server` reports `prompt_n` (tokens processed) and `cache_n` (tokens reused) in its `timings`. I logged both per step and asserted in a test that the prefix hash stays the same.
Aperio story: removing 2 tool schemas mid-conversation dropped reuse to `cache_n=12577` of `prompt_n=31067` and cost **262 s** of prefill on one turn (`lib/agent/tool-profiles.js`).

**Q6. Local KV cache vs cloud prompt caching?**
A: Same idea, different control. Locally, llama.cpp reuses the KV cache of its slot automatically when the prefix matches. In the cloud, for example Anthropic, you mark **cache breakpoints** (`cache_control`) on stable blocks. Cached input tokens are billed at a fraction of the price and are faster, for a limited time (minutes by default). Aperio sets a breakpoint on the system prompt (`lib/agent/providers/anthropic.js`).

**Q7. What is cache warm-up?**
A: Send a hidden first request with the real system prompt before the user's first message, so the prefix is already in the cache. Aperio does this and also uses a static greeting, so a greeting never costs model time (`lib/agent/index.js`, `warmCache`).

**Q8. Several users on one llama-server — what happens to the cache?**
A: The server has a limited number of **slots**, each with its own KV cache. Users compete for them, and switching users evicts cached prefixes. A shared, identical system prompt helps every user. At scale you route each session to the same slot or server ("sticky" routing).

## B. Context window and trimming

**Q9. Why not just use a model with a huge context window?**
A: Cost and latency grow with every token on every step, and quality drops on very long contexts: models miss things in the middle. Aperio caps its warning threshold near 80k tokens on any window size for this reason (`lib/context/trim.js`).

**Q10. How do you trim safely?**
A: Count tokens. Act in stages (warn → trim → summarise → emergency). Drop the oldest turns first. Never keep a tool call without its result, or a result without its call: providers reject that. Never trim the system prompt, the newest user message or the pinned task state.

**Q11. What is "pinned task state" and why do you need it?**
A: A short message that is rebuilt every step from the database: goal, steps done, pending approvals with their tokens, and the next expected step. Trimming can remove old messages, but this note always survives. Without it, the agent forgets the pending refund and proposes it again.

**Q12. Summarise vs drop?**
A: Dropping is cheap and exact, but you lose information. Summarising keeps the gist, but it costs a model call, it can be wrong, and it changes the prefix, so the cache breaks once. Use summaries for long chats. For money facts (amounts, ids, tokens), always use exact pinned state, never a summary.

**Q13. Trimming also breaks the KV cache. How do you limit that?**
A: Trim in big steps, not one message at a time. For example, when you reach 75%, cut down to 50%. Then the prefix changes rarely and the cache rebuilds once, not on every step.

## C. Big tool results

**Q14. A tool returns 5,000 rows. What do you do?**
A: First, design the tool to page: a maximum page size and a cursor. If a big result still arrives, offload it: store the full result, and give the model a preview and a `result_id` for a paging read tool. Aperio offloads results over 20k tokens, or 25% of the window (`lib/context/toolResultOffload.js`).

**Q15. Why is offloading also a security point?**
A: Big raw results can carry secrets or untrusted instructions. Aperio redacts secrets in the stored result (`redactSecrets` in `toolResultOffload.js`). A short preview also limits how much injected text reaches the model.

## D. Tool-result caching

**Q16. Which tool results may you cache?**
A: Only **read** tools whose result does not change during the cache lifetime. Use the key `(tool name, canonical arguments)`, a short TTL, and remove the entry when a write touches the same object (a refund removes `get_payment(42)`). **Never cache writes.** Writes are protected by idempotency keys instead.

**Q17. Why "canonical" arguments?**
A: `{"id":42,"x":1}` and `{"x":1,"id":42}` mean the same thing. Sort the keys and normalise values before hashing, or identical calls miss the cache.

**Q18. In-turn dedup vs a TTL cache?**
A: In-turn dedup reuses a result only inside one turn. It is very safe, and it breaks "model calls the same thing again" loops. Aperio does this (`findPriorToolResult` in `lib/tools/executor.js`). A TTL cache lives across turns, so it is faster, but you must handle invalidation. Start with in-turn dedup.

**Q19. Can a cached result make the agent wrong?**
A: Yes, if it is stale. The payment was captured a second ago, but the cache still says "authorised". That is why there is a short TTL, invalidation on writes, and a fresh read **inside the write path**: the API checks the real state before any money moves.

## E. Cost and tokens

**Q20. How do you estimate tokens in .NET?**
A: Use a tokenizer that matches the model (for example, `Microsoft.ML.Tokenizers`), or the usage numbers the server returns. Estimates are for budgeting. Use the server's numbers for billing and logs.

**Q21. What would you put on a cost dashboard for agents?**
A: Input, output and cached tokens per task; cache hit ratio (`cache_n / prompt_n`); steps per task; prefill time per step; offloads per task. A falling cache hit ratio is often the first sign that someone added a timestamp to the prompt.


## Aperio: have you solved this before?

**Q-APERIO. "Have you solved this before?"**
A: Yes, and with numbers. In Aperio I measured a KV-cache break: removing two tool schemas mid-conversation dropped reuse to 12,577 of 31,067 tokens and cost 262 s of prefill on one turn, so I keep tool schemas stable. Aperio also warms the cache, trims in stages with an 80k absolute cap, offloads big tool results and dedups repeated calls. What it lacks is a read-tool TTL cache with write invalidation, and a pinned task ledger — this lab adds both.
Details and file references: the "Aperio comparison" section in this lab's `README.md`.
