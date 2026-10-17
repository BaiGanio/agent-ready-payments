# Lab 15 — Context and caching: KV cache, trimming, offload, tool-result cache

## What we solve
An agent resends the **whole conversation** on every step. That gets slow and expensive, and when the context fills up, the agent starts to forget. This lab keeps the agent fast and stops it losing what matters.

## Simple example
A support agent works through 30 refund questions in one session. Each step sends the system prompt, 3 tool schemas and all the history to `llama-server`.

- **With a stable prefix:** the system prompt and tool schemas are byte-for-byte the same on every request. llama.cpp keeps the already-processed tokens in its **KV cache** and only processes the new part. Each step takes about 1 second.
- **Break the prefix:** someone puts `Current time: 14:02:11` at the top of the system prompt, or the tool list order changes. Now every request looks new, the whole prompt is processed again, and a step takes 20 seconds. Same answers, 20× slower.
- **Context full:** after 30 questions the history is bigger than the context window. You must trim. If you simply drop the oldest messages, you may drop the message that held the refund **proposal token**. The agent then "forgets" it has a pending refund and proposes it again.
- **Big tool result:** `list_payments` returns 5,000 rows. That would fill the context in one step. Instead, keep the full result outside the context and give the model a short preview plus a handle (`result_id`) it can page through.
- **Repeated reads:** the model calls `get_payment(42)` three times in one turn. The second and third calls use the cached result. But after `refund_payment` on payment 42, that cache entry must be removed.

## Everyday picture
**A student reading a long book for an exam.**
- **KV cache:** the student keeps a bookmark and doesn't re-read chapters 1–9 every time chapter 10 starts. If someone changes one word on page 1 (the timestamp), the student thinks it's a new book and starts again from page 1.
- **Trimming:** the student's desk only holds 20 pages. Old pages go back on the shelf, but the **sticky note with the exam question and the homework still due** stays on the desk. That note is the pinned task state.
- **Offload:** a 500-page appendix stays in the library. The student writes down "appendix, page 212" and fetches it when needed.
- **Tool cache:** if you already looked up the same word in the dictionary a minute ago, you don't look it up again. But if the dictionary got a new edition (a write happened), you look again.

## The shape (kept small)
- **Measure first.** `llama-server` returns `timings` with `prompt_n` (tokens processed) and `cache_n` (tokens reused from the KV cache). Log both for every step. Aperio uses exactly these two numbers.
- **Stable-prefix rules:** the system prompt holds no timestamps, no ids and no random order. Tool schemas are sorted and serialised the same way every time. Things that change go at the **end**. Test it: hash the prefix bytes and assert that they are equal across steps.
- **Context budget:** count tokens, and act at stages: warn, then trim, then summarise.
- **Pinned task state:** a short "task ledger" message (goal, steps done, pending approvals, next expected step) is rebuilt from `AgentStep` on every request and is **never trimmed**.
- **Safe trimming:** drop the oldest turns first, but never split a tool call from its result. Drop orphaned tool results.
- **Offload:** a tool result over N tokens is stored, and the model gets a preview plus `result_id`. One extra read tool, `read_result(result_id, page)`, pages through it.
- **Tool-result cache:** only for **read** tools, keyed by tool name + canonical arguments, with a short TTL, and invalidated by writes on the same payment. Never cache writes.
- **Cloud prompt caching** (Anthropic `cache_control` breakpoints): discussion only. Aperio has it (`lib/agent/providers/anthropic.js`).

## Aperio comparison (our backbone)
- **KV cache story:** removing 2 tool schemas in the middle of a conversation shifted the whole prompt. llama-server reused only `cache_n=12577` of `prompt_n=31067` tokens, and one turn spent **262 s** re-reading the prompt and failed its 600 s limit. Fix: keep tool schemas stable (`lib/agent/tool-profiles.js` ~55–68).
- **Cache warm-up:** a hidden first request fills the KV cache before the user types. The greeting is static text, so it doesn't spend model time (`lib/agent/index.js` ~1045–1080).
- **Staged context limits:** warn 60%, hand off 72%, trim 75%, summarise 80%, emergency 90%, with an absolute cap near 80k tokens, because quality drops past that point on any window size (`lib/context/trim.js`).
- **Offload:** tool results over 20k tokens (or 25% of the window) become a preview plus a stored artifact (`lib/context/toolResultOffload.js`).
- **Dedup:** an identical tool call in the same turn reuses the earlier result (`lib/tools/executor.js`, `findPriorToolResult`).

**Related:** the *first* request with an empty KV cache is a cold start. Lab 17 covers it at every layer.

## Why it matters for a payment platform
At payment-platform volume, tokens are money and latency is customer experience. "Why did the agent get slow and start repeating itself after an hour?" is the first production bug every agent team meets. Knowing KV caching, trimming and offload from real numbers makes you the person who can fix it.

## Done when
- A log shows `prompt_n` and `cache_n` per step. With the stable prefix, `cache_n` covers most of `prompt_n` after the first step.
- A test proves the prefix bytes are identical across steps, and that a timestamp in the system prompt breaks it.
- A 40-turn scripted conversation stays under the token budget, and the pending refund token survives every trim.
- A 5,000-row result is offloaded, and the model pages through it with `read_result`.
- `get_payment(42)` is served from cache on repeat, and is fetched fresh after a refund on payment 42.

Spec + failing tests: written when the lab starts.
