# Lab 05 — Bare ADO.NET

## What we solve
Move a lot of data fast, and read a lot of data without running out of memory.

## Simple example
At night the acquirer sends a file with 1,000,000 settled transactions.
- Insert them one by one: 1,000,000 round trips. Takes ages.
- `SqlBulkCopy`: streams them in big batches. Seconds.

Then a report reads all 1,000,000 back.
- Load into a `List<T>`: all rows in memory at once.
- Stream with `SqlDataReader`: one row at a time. Memory stays flat.

## Why it matters for a payment platform
Legacy .NET Framework payment code is full of raw ADO.NET. You must read it, fix it, and know when Dapper is not enough. Dapper is a thin layer on top of exactly this.

## Everyday picture
**Moving house.** Carrying one box at a time to the new flat = row-by-row `INSERT`. Hiring a truck = `SqlBulkCopy`. Reading the report: carrying the whole wardrobe at once (`List<T>`) vs one drawer at a time (streaming `DataReader`).

## EF Core angle
Load the same data three ways and time them: EF `AddRange` + `SaveChanges`, EF `ExecuteUpdateAsync` (one set-based statement), and `SqlBulkCopy`. Quote your own ratios in the interview.

## Aperio comparison (our backbone)
| Case | Aperio | Where (verified 2026-10-17) | What this lab adds |
|---|---|---|---|
| Raw driver access, no ORM | ✅ Has | `pg` and `better-sqlite3` with hand-written SQL in both stores | ADO.NET: `SqlCommand`, `SqlDataReader` |
| Batch insert | ✅ Has | `bulkInsert()` (`db/postgres/store.js` 160) | `SqlBulkCopy` at 1M rows, TVPs |
| Streaming huge reads | ❌ Not claimed | — | Constant-memory streaming with `DataReader` |
| Measured numbers | ❌ No | — | Row-by-row vs batch vs bulk, list vs stream |

## Done when
Bulk load and streaming read work; you have your own numbers for row-by-row vs bulk, and list vs stream.

Spec + failing tests: written when the lab starts.
