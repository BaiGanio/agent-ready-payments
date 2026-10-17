# Lab 04 — SQL Server stored procedures + locking

## What we solve
Two things try to change the same payment at the same time. Only one may win. And the database must not lock up.

## Simple example
- **Race:** two servers both try to capture payment 42. Both read "Authorised", both write "Captured". The money is captured twice. Fix: one `UPDATE … WHERE Status = 'Authorised'` — only one can win.
- **Deadlock:** job A locks payment 1, then wants payment 2. Job B locks payment 2, then wants payment 1. Both wait forever. SQL Server kills one (error 1205). You will make this happen on purpose, then fix it by always locking in the same order.
- **Queue:** 5 workers take jobs from one table. With `READPAST` each worker skips rows others are holding — no waiting.

## Why it matters for a payment platform
The ad says it word for word: *"SQL Server stored procedures for highly concurrent and multithreaded production environments."*

## Everyday picture
- **Race:** two people buy the last concert ticket at the same second. Without a rule, both get it. The rule: "sell only if still available" in one move — the conditional `UPDATE`.
- **Deadlock:** two cars at a narrow bridge, each waiting for the other to back up. Fix: everyone drives in the same direction first (lock in the same order).
- **READPAST:** a supermarket with many tills. You don't queue behind a busy till; you walk to a free one.

## EF Core angle
EF Core optimistic concurrency: `[Timestamp]` on a `rowversion` column → `DbUpdateConcurrencyException` on conflict. And call the capture proc from EF with `ExecuteSqlAsync`. Note: EF has no `UPDLOCK`/`READPAST` hints — for those, raw SQL or a proc.

## Stretch: the same rule over HTTP — ETag and If-Match
Idea borrowed from ScroogeCorp B03 + B07 ("Two approvers cannot both win", "ETag").

`rowversion` protects one save. But the race often starts earlier, in a **person's or an agent's head**: they read the payment, think for a minute, then act on what they read. Meanwhile someone else changed it.
- `GET /payments/{id}` returns `ETag: "7"`. The ETag is the row's version. Do not hash the body.
- `POST /payments/{id}/capture` (and refund) with `If-Match: "7"`. Version still 7 → it runs. Version is now 8 → `412 Precondition Failed`, a `stale_version` ProblemDetails, and nothing changes.
- `GET` with `If-None-Match: "7"` and no change → `304 Not Modified`, no body.
- **Which version?** SQL Server: `rowversion` (convert it to a string for the header). Postgres API: an explicit `version int` column, bumped in the same `UPDATE`. That way one ETag rule works on both engines. Postgres `xmin` works too, but only on Postgres, and the value can wrap around.
- **Two reasons, one status family:** a domain refusal ("already captured") is a `409`. A stale version is a `412`. Tests must tell them apart by the `code`, not by luck.
- **Agent link:** Lab 08's refund token should bind to this version. The human approves *"refund 80 EUR on payment 42 at version 7"*. If the payment changed after that, the confirm fails. It does not refund what the human never saw.

## Aperio comparison (our backbone)
| Case | Aperio | Where (verified 2026-10-17) | What this lab adds |
|---|---|---|---|
| SQL Server, stored procedures | ❌ No | Postgres and SQLite only | Everything T-SQL |
| Pessimistic row lock | ✅ Has (Postgres) | `SELECT … FOR UPDATE` in `approvePending()` (`db/postgres/store.js` 238) | `UPDLOCK, ROWLOCK` in SQL Server |
| Compare-and-swap update | ✅ Has | `claimAgentInterrupt` conditional `UPDATE … RETURNING` (`db/postgres/store.js` 1151) | Same in a capture proc with `@@ROWCOUNT` |
| Queue claim that skips locked rows | ❌ No | — | `READPAST` (and `SKIP LOCKED` in Lab 13) |
| Deadlock made, found and fixed | ❌ No | — | Deadlock on purpose, 1205, lock ordering |
| Isolation levels, RCSI, `rowversion` | ❌ No | default isolation only | All of it |

## Done when
The deadlock test fails before your fix and passes after. The capture proc survives a parallel test.
- *Stretch:* two clients read version 7. The first capture wins. The second gets `412 stale_version`. A fresh read shows the winner's state.

Spec + failing tests: written when the lab starts.
