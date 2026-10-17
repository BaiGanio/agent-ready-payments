# Lab 02 — Minimal API + Dapper (Postgres)

## What we solve
Put the state machine behind an HTTP API that is **predictable**: same input, same answer, clear errors.

## Simple example
A client sends `POST /payments/42/refund` with €500 on a €100 payment.
- Bad API: `500 Internal Server Error` and a stack trace. The client — or an AI agent — has no idea what to do.
- Good API: `409` with `{"code": "refund_exceeds_captured", "detail": "max refundable is 40.00 EUR"}`. The caller knows exactly what went wrong and what is allowed.

## Why it matters for a payment platform
The ad asks for *"backend services with clear and deterministic interfaces that can be used within AI-driven workflows."* This lab is that sentence in code. Dapper (not EF) because the job is a wrapper over an existing database.

## Everyday picture
- **Vending machine:** B4 = Snickers, every time. The bad machine has one "GO" button, a hidden dial, says "THANK YOU" when it fails, and setting 3 phones your mother. That is a fat `/process` endpoint. The fix is more buttons, each labelled.
- **Hotel reception:** a shrug, a long apology in French, or a printed card "ROOM NOT READY — code 409". Only the card helps. ProblemDetails is that card.

Code sketches: `AGENT-READY-BACKEND.md` §4 (bad vs good endpoint) and §6 (one error shape).

## EF Core angle (stretch)
Write a second `IPaymentStore` with EF Core. Run the **same** integration tests against both. You will see where EF saves code (aggregate save) and where Dapper is clearer (hand-written list query).

## API shape
Minimal API with `MapGroup`. **CQRS-lite:** commands (create, capture, refund) go through the domain; queries (get, list) are Dapper straight to DTOs. Validation starts with .NET 10 built-in validation; FluentValidation comes in Lab 03. See PLAN.md "API shape".

## Stretch: keyset paging for the list query
Idea borrowed from an earlier lab of mine (ScroogeCorp B04, "Stable order and real paging").

`GET /payments?merchant=M1&limit=50&cursor=…` returns `{ "items": [...], "nextCursor": "…" }`.
- **Stable order:** `ORDER BY created_at DESC, id DESC`. `id` is unique, so there are never ties. Order by `created_at` alone, and two rows from the same millisecond can swap places between two identical calls.
- **Keyset, not offset:** `WHERE (created_at, id) < (@afterCreatedAt, @afterId) LIMIT @limit`. Same cost on page 1 and page 500. No skipped or doubled rows when new payments arrive at the top.
- **Opaque cursor:** the last row's `(created_at, id)`, base64-encoded. Clients (and agents) pass it back; they never build it.
- **Hard limits:** `limit` is clamped to 100. `0` or a negative value falls back to the default. A broken cursor is a `400 invalid_cursor`. It is never a 500, and never silently page 1.
- **The honest limit:** keyset is not a snapshot. A payment captured while you are on page 3 shows its new status on page 4. Write that in a code comment.

Tests: 5 rows with the **same** `created_at` come back in the same order 20 times in a row; walking all pages returns every row exactly once, even with inserts between page calls; clamp, default and bad-cursor cases.

## Aperio comparison (our backbone)
| Case | Aperio | Where (verified 2026-10-17) | What this lab adds |
|---|---|---|---|
| Typed tool contracts | ✅ Has | MCP tools with zod schemas (`mcp/tools/*.js`) | Typed HTTP contracts in C# |
| Real HTTP status codes | ✅ Has | routes in `lib/routes/` return 400/404/409/500 … | — |
| One standard error shape | ⚠️ Partly | bodies are `{ error: "…" }`, mostly the same key, but not RFC 9457 ProblemDetails; no stable `type` URI or `code` field | ProblemDetails with a stable `code` from one place |
| One interface, two data engines | ✅ Has | `db/index.js` + `db/sqlite/store.js` + `db/postgres/store.js` | `IPaymentStore` with Dapper and EF Core behind it |
| Parameterised raw SQL | ✅ Has | `$1` parameters in `db/postgres/store.js`, prepared statements in SQLite | Dapper parameters, explicit types |

## Done when
Create / capture / refund / get endpoints work; integration tests run against a real Postgres.
- *Stretch:* `GET /payments` pages with a keyset cursor; the "same timestamp" and "walk every page" tests pass.

Spec + failing tests: written when the lab starts.
