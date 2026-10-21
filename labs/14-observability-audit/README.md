# Lab 14 — Observability and audit for agents

## What we solve
When an agent does something, you must be able to see **every hop** it took and **prove** later who did what.

## Simple example
Tuesday morning a merchant phones: *"Your system refunded €80 on Friday that we never approved!"*

Without tracing and audit you have: one API log line, `POST /payments/42/refunds 201`. Who sent it? A person? An agent? Which agent, why, with what approval?

With this lab you open **one trace** and see the whole chain:

```
Agent run 7f3a  (agent key: support-bot, scope: refund)
 ├─ model call   gemma-4-E4B   1.8 s   tokens in 2,140 / out 96
 ├─ tool         list_payments          → 2 payments
 ├─ tool         refund_payment         → proposal, token rf_9x2
 ├─ approval     by user "maria"        14:02:11
 └─ tool         refund_payment confirm
     └─ HTTP     POST /payments/42/refunds   201
         └─ SQL  INSERT PaymentOperation …   4 ms
```

The audit table also has a row saying that agent `support-bot` refunded €80 on payment 42, approved by `maria`, with the same trace id. The phone call is answered in two minutes.

## Everyday picture
**A parcel tracking number.** One number follows the parcel through every depot, truck and doorstep scan. When it goes missing, you see the last scan. A **trace id** is that number for one request across the agent, the MCP server, the API and the database.

## The shape (kept small)
- **OpenTelemetry** in every process: `Payments.Agent`, `Payments.Mcp`, `Payments.Api`, plus Npgsql / SqlClient instrumentation for SQL spans.
- **One trace across process borders:** the agent starts the trace. The trace context (W3C `traceparent`) travels with the tool call to `Payments.Mcp`, which sends it on as an HTTP header to the API.
- **Model-call spans** with the OpenTelemetry GenAI conventions (`gen_ai.*`): model name, token counts, duration. **No prompt text in spans by default**, because prompts can contain customer data.
- **Metrics:** tool calls per tool, tool error rate, step-cap hits, approval reject rate, model latency.
- **A local viewer** in Docker: the .NET Aspire dashboard (standalone) or Jaeger.
- **Audit** (the table is built **here** — moved from lab 08 on 2026-10-21): one `agent_audit` row per agent tool call, written by the **API**, with trace id, agent key, scope, tool, redacted arguments, result code and approver. A `403 insufficient_scope` is audited too. Every agent call leaves exactly one row.

## Why it matters for a payment platform
The ad says the platform *"increasingly interacts with AI agents and orchestration layers"*. In a regulated payments company, "the agent did it" is not an answer. You must show who, what, when, why and with whose approval. Tracing also finds slow hops and runaway agents.

## Aperio comparison (our backbone)
| Case | Aperio | Where (verified 2026-10-17) | What this lab adds |
|---|---|---|---|
| Structured lifecycle traces | ✅ Has | `lib/agent/lifecycle-trace.js`, `lib/agent/turn-diagnostics.js` | — |
| Safety events | ✅ Has | `tool_repeat_break`, `tool_step_limit`, `tool_budget_exhausted` emitted to the client | The same as OpenTelemetry metrics |
| Run records | ✅ Has | `agent_runs` (verdict, model, tools JSONB, duration) | — |
| Redaction before the model | ✅ Has | `lib/privacy/redact.js`, `lib/agent/redaction-relay.js` | No prompt text in spans |
| OpenTelemetry | ❌ No | none in `lib/`, `mcp/` or `package.json` | Traces across agent → MCP → API → SQL |
| Per-call audit table | ❌ No | — | `AgentAudit` with trace id |

## Done when
- One refund through the agent produces **one** trace with spans from all three processes and SQL.
- The audit row and the trace share the same trace id.
- Every agent call leaves exactly one audit row, and a read-only key's refused refund is one of them.
- A test shows that card numbers and prompt text never appear in spans or logs.
- The metrics show a step-cap hit after you run a looping fake model.

Spec + failing tests: written when the lab starts.
