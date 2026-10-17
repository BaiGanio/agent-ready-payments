# Lab 03 — Idempotency

## What we solve
A retry must never charge the customer twice.

## Simple example
The shop sends "charge €80". The network times out. The shop doesn't know if it worked, so it sends "charge €80" again.
- Without idempotency: two charges. The customer pays €160 and calls their bank. Chargeback.
- With idempotency: both requests carry the same `Idempotency-Key: 7f3a…`. The second one gets the **same answer** as the first. One charge.

Now make it hard: 50 copies of that request arrive **at the same moment**. Still exactly one payment.

## Why it matters for a payment platform
It is the classic payments interview question. It also matters for AI agents: agents retry a lot, so every write tool they call must be idempotent.

## Everyday picture
**Double-tap on the card reader.** You pay for coffee, the reader beeps oddly, you tap again. A good shop has your receipt number written down, sees it again, and shows the same receipt. One coffee, one charge. The agent is a nervous customer who taps twice a lot.

The race: two tills check the list at the same instant, both see "not there yet", both charge. A clipboard check is not enough. You need a till that **physically refuses a duplicate number** — the unique index.

Code sketch: `AGENT-READY-BACKEND.md` §5 (EF Core and Dapper versions).

## EF Core angle
Build it twice: Dapper insert-first (main) and EF Core check + `DbUpdateException` catch. The same 50-parallel test must pass for both.

## Aperio comparison (our backbone)
| Case | Aperio | Where (verified 2026-10-17) | What this lab adds |
|---|---|---|---|
| `Idempotency-Key` header | ❌ No | — | The full header pattern with a unique index |
| Same call twice in one turn → one execution | ✅ Has | `findPriorToolResult()` reuses the earlier result (`lib/tools/executor.js` 469) | — |
| An approved action runs exactly once | ✅ Has | conditional claim `UPDATE … WHERE status IN ('approved','edited')` (`db/postgres/store.js` 1151) | — |
| Check-then-act race found and fixed | ✅ Has | `approvePending()`: two approvals could both pass the `status='pending'` check. Fixed with `SELECT … FOR UPDATE` + conditional `UPDATE` in a transaction (`db/postgres/store.js` 238; SQLite: one `db.transaction`) | The same race on payment create, under 50 parallel requests |
| Idempotent import | ✅ Has | `mcp/tools/data.js`: memories matched by id, wiki by slug, so running twice changes nothing | — |

## Done when
The parallel test (50 identical POSTs) creates exactly one payment and every response points to it.

Spec + failing tests: written when the lab starts.
