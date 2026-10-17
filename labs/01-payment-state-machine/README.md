# Lab 01 — Payment state machine (TDD)

## What we solve
A payment can only move in certain ways. The code must make the wrong moves impossible.

## Simple example
- Payment is **Authorised** for €100. Capture €100 → OK, now **Captured**.
- Try to **Void** it now → must fail. The money already moved. You need a refund.
- Refund €60 → OK. Refund €50 more → must fail. Only €40 is left.

With three `bool` flags (`IsCaptured`, `IsVoided`, `IsRefunded`) a bug can set two at once. With one explicit state and a transition table, that bug can't exist.

## Why it matters for a payment platform
Every payment platform has this machine at its heart. A wrong move here is lost money or a double refund. Writing it test-first is also the cleanest way to show TDD in the interview.

## Everyday picture
A **parcel tracker**: Ordered → Packed → Shipped → Delivered. A parcel can't go from Delivered back to Packed, and it can't be Shipped and Cancelled at once. The tracker refuses the wrong move and says why. That is all a state machine is.

## Aperio comparison (our backbone)
| Case | Aperio | Where (verified 2026-10-17) | What this lab adds |
|---|---|---|---|
| Explicit status machine in the DB | ✅ Has | `db/migrations/008_agent_interrupts.sql`: `status` `CHECK` with 9 values; transitions in `lib/security/interruptService.js` | The same idea for payments: authorise → capture → refund / void |
| Illegal transition refused | ✅ Has | `decide()` on a non-pending interrupt throws `InterruptConflictError`; `FINAL_STATUSES` set | A **Result type** instead of exceptions for business outcomes |
| Injected clock | ✅ Has | `createInterruptService({ now = () => new Date() })` | `TimeProvider` in .NET |
| Money amounts and limits | ❌ No | — | Capture ≤ authorised, refunds ≤ captured |
| Test-first proof | ⚠️ Honest | Aperio has many tests, but I can't prove they were written first | Strict red → green → refactor, visible in the commits |

## Done when
All transition and amount tests (given by the agent) are green. No DB, no clock calls, no framework in the domain.

Spec + failing tests: written when the lab starts.
