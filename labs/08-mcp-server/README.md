# Lab 08 — The C# MCP server (`Payments.Mcp`)

## What we solve
Let an AI agent use the payment API **safely**: it can look things up freely, but it can never move money by accident.

## Simple example
A support person types to an agent: *"Customer says they were charged twice for order 1234, refund the duplicate."*

- The agent calls `list_payments` → finds two captured payments.
- It calls `refund_payment` for one of them → the server does **not** refund. It returns a proposal: *"Refund 80.00 EUR on payment 42? Token: rf_9x2"*.
- The human says yes → the agent confirms with the token → one refund happens. If the agent repeats the confirm by mistake → still one refund (idempotency).
- If the agent sends `amount: "80,00 euro?"` → refused. Money tools never guess.

## Why it matters for a payment platform
This is the role's AI half: *"APIs structured to serve as reliable, deterministic tools within AI agent workflows."* Almost no other .NET candidate will have built one. It stays deliberately small: 3 tools, stdio, calls the API over HTTP.

## Everyday picture
- **The list on the kitchen door:** the blindfolded chef (model) can only ask for what is on the list. "Call the bank" → "Not on my list." No is an answer, not a crash.
- **The IKEA label:** a drawer labelled "stuff" vs "sharp knives — handles to the left — do not reach in blind". The tool description is the label, and it is real code.
- **"Shall I put it through?":** the chef shouts "bin the whole fish delivery". A gated tool writes a note and waits for the manager.

Code sketches: `AGENT-READY-BACKEND.md` §3 (registry, one tool) and §7 (description is code).

## Agent identity and audit
- Each agent gets its **own API key** with **scopes**: `payments:read` or `payments:refund`. The key lives in the MCP server's environment, never in tool arguments.
- The **API** checks the scope. A read-only agent that calls refund gets `403 insufficient_scope`.
- The **API** writes one `AgentAudit` row per agent call: agent, tool, payment id, redacted arguments, result, approver and trace id. The MCP server still never touches the database.

## Paging the agent can follow
`list_payments` wraps Lab 02's keyset list (stretch there, required here). It returns at most 20 items and a `next_cursor`. The tool description tells the model: *"If `next_cursor` is not null, there are more results. Pass it back as `cursor` to get them. Do not invent a cursor."* A cursor the model made up gets `invalid_cursor`, not page 1. Stable order matters even more for an agent than for a person. If the order changes between two calls, the model may "find" the same duplicate charge twice, or miss it.

## Aperio comparison (our backbone)
| Case | Aperio | Where (verified 2026-10-17) | What this lab adds |
|---|---|---|---|
| MCP server | ✅ Has (JS) | `mcp/index.js`, 14 tool modules in `mcp/tools/`, zod schemas | Same in C# with the official SDK |
| Two-phase confirm for writes | ✅ Has | `db_execute` propose → token (5 min) → approve → re-validate → digest → claim (`lib/handlers/database/databaseHandlers.js`, `lib/security/interruptService.js`) | Same for `refund_payment` |
| Refuse, don't repair, for destructive tools | ✅ Has | `lib/tools/executor.js` 705 | Same for money arguments |
| Dangerous tools off by default | ✅ Has | `APERIO_ENABLE_SHELL` (`mcp/tools/shell.js` 76) | — |
| Untrusted output fenced, writes tainted | ✅ Has | `lib/agent/tool-safety-middleware.js` | — |
| MCP server goes only through an API | ❌ Different by design | Aperio's tools use the store directly through `ctx` (Aperio *is* the backend) | `Payments.Mcp` calls the API over HTTP and never the DB |
| Per-agent identity and scopes, enforced | ⚠️ Partly | tool profiles (`lib/agent/tool-profiles.js`), `lib/security/agentPermissions.js`; but `id/reference/tech-debt.md` line 195 says AgentSpec filesystem/memory scoping is **not enforced at execution time** | API keys with scopes, enforced by the API |
| Audit row per tool call | ⚠️ Partly | `agent_runs.tools` (JSONB per job run); tool-failure ledger `var/toolrepair/events.tsv` (`logToolCallFailure`) | `AgentAudit` row per call, with trace id |

## Done when
- Claude Code can connect to `Payments.Mcp` and do the scenario above. Unit tests cover each tool.
- A read-only key cannot refund (`403 insufficient_scope`), and that attempt is audited too.
- Every agent call leaves exactly one audit row.
- `list_payments` returns `next_cursor`. A test walks all pages through the tool and sees every payment exactly once. An invented cursor gets `invalid_cursor`.

Spec + failing tests: written when the lab starts.
