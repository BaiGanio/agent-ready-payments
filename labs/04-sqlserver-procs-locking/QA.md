# Lab 04 — Interview Q&A: SQL Server stored procedures, locking, concurrency

The ad says: *"Develop and optimize SQL Server stored procedures for highly concurrent and multithreaded production environments."* This lab is that sentence.

---

## A. Stored procedures

**Q1. Why stored procedures in a payments platform?**
A: One round trip for multi-statement logic, a stable contract the DBA can tune without redeploying the app, permission control (grant `EXECUTE` only, no table access), cached plans. Downside: logic in two places, harder to test and version — so keep procs in source control with migrations and tests.

**Q2. What do you put at the top of every proc?**
A: `SET NOCOUNT ON;` (no "rows affected" chatter), `SET XACT_ABORT ON;` (any error rolls back the whole transaction, no half-done work), then explicit `BEGIN TRY / BEGIN TRAN … COMMIT / END TRY BEGIN CATCH … IF @@TRANCOUNT > 0 ROLLBACK; THROW; END CATCH`.

**Q3. What is parameter sniffing?**
A: The plan is compiled for the first parameter values seen, then reused. If those values were unusual (one huge merchant), the plan can be terrible for everyone else. Fixes: `OPTION (RECOMPILE)` for rare expensive queries, `OPTIMIZE FOR`, local variables, or (2022) Parameter Sensitive Plan optimisation.

**Q4. How do you return errors from a proc?**
A: `THROW 50001, 'amount_exceeds_captured', 1;` with a documented error number range. The app maps the number to the API error code. Or return a status result set. Never rely on `RETURN` codes alone — easy to ignore.

## B. Locking

**Q5. Write the capture proc without a race.**
A: One conditional update, which is atomic:
```sql
UPDATE dbo.Payments
SET Status = 'Captured', CapturedAmount = @Amount, UpdatedAt = SYSUTCDATETIME()
WHERE PaymentId = @PaymentId AND Status = 'Authorised' AND @Amount <= AuthorisedAmount;
IF @@ROWCOUNT = 0 -- read why, then THROW the right error
```
The `WHERE` holds the state check, so two concurrent captures can't both succeed.
Aperio link: the same compare-and-swap shape claims a confirmed agent action — `UPDATE agent_interrupts SET status='claimed' WHERE id=$1 AND status IN ('approved','edited')` (`db/postgres/store.js`, `claimAgentInterrupt`).

**Q6. When do you need `UPDLOCK`?**
A: When you must *read* first and update later in the same transaction (read-modify-write). `SELECT … WITH (UPDLOCK, ROWLOCK) WHERE PaymentId = @Id` takes an update lock, so a second transaction doing the same waits instead of reading stale data. Without it, two readers both take shared locks, both try to upgrade to exclusive → **conversion deadlock**.

**Q7. What does `READPAST` do and where is it useful?**
A: Skips rows locked by others instead of waiting. Classic **queue pattern**: many workers claim jobs with `UPDATE TOP (@n) … WITH (ROWLOCK, READPAST, UPDLOCK) … OUTPUT inserted.*`. Each worker gets different rows, nobody blocks.
Trap: "Why not use `NOLOCK`?" → `NOLOCK` = read uncommitted: dirty reads, missed or doubled rows during page splits. Never for money.

**Q8. Lock granularity and escalation?**
A: Row → page → table. After ~5,000 locks on one object in one statement, SQL Server may escalate to a table lock, which blocks everyone. Keep transactions small; batch big updates (e.g. 1,000 rows at a time).

**Q9. Lock modes you should know?**
A: S (shared, reads), U (update, "I will write"), X (exclusive, write), IS/IX/IU (intent locks on higher levels). U is compatible with S but not with another U — that is why `UPDLOCK` prevents the conversion deadlock.

## C. Deadlocks

**Q10. What is a deadlock and what does SQL Server do?**
A: Two transactions each hold a lock the other needs. The lock monitor picks a **victim** (cheapest to roll back), kills its transaction with error **1205**.

**Q11. How did you make one on purpose?**
A: Transaction A updates payment 1 then payment 2. Transaction B updates payment 2 then payment 1. Run both in parallel with a small delay between the two updates. One gets 1205.

**Q12. How do you fix deadlocks?**
A: (1) Access objects in the **same order** everywhere (e.g. order by id). (2) Shorter transactions. (3) Right indexes, so you lock fewer rows. (4) `UPDLOCK` for read-then-write. (5) Snapshot isolation for readers. (6) As a last line, **retry on 1205** in the app — with a limit and backoff.

**Q13. How do you investigate a deadlock in production?**
A: The `system_health` Extended Events session captures deadlock graphs by default. Read the graph: which resources, which statements, which lock modes.

## D. Isolation and optimistic concurrency

**Q14. Name the isolation levels.**
A: READ UNCOMMITTED, READ COMMITTED (default), REPEATABLE READ, SERIALIZABLE, SNAPSHOT, and READ COMMITTED SNAPSHOT (RCSI, a database option).

**Q15. What is RCSI and why turn it on?**
A: Readers see the last committed version from the version store (tempdb) instead of waiting for writers. Readers don't block writers and writers don't block readers. Cost: tempdb load, and a reader can see slightly old data.
Trap: "Does RCSI stop lost updates?" → No. Two writers still conflict. You still need `UPDLOCK` or a conditional update or `rowversion`.

**Q16. `rowversion` — how do you use it?**
A: An auto-changing binary column. Read it with the row; update `WHERE PaymentId = @Id AND RowVer = @RowVer`. `@@ROWCOUNT = 0` → someone else changed it → 409 Conflict, client reloads. That is **optimistic** concurrency.

**Q17. Optimistic vs pessimistic — which for payments?**
A: Pessimistic (`UPDLOCK`) for hot short operations on one row where conflicts are expected (capture). Optimistic (`rowversion`) for user edits with low conflict and long think time. Conditional single-statement updates beat both when the logic fits in one `WHERE`.

**Q18. What does SERIALIZABLE do that READ COMMITTED doesn't?**
A: Range locks: prevents phantoms (new rows appearing in a range you read). Expensive and deadlock-prone. Use `HOLDLOCK` on one specific query instead of the whole transaction level when you need it (e.g. upsert).

## E. Performance

**Q19. A proc became slow overnight. Steps?**
A: Check the actual plan (Query Store shows plan changes and regressions). Parameter sniffing? Stale statistics? Missing index? Blocking (`sys.dm_exec_requests`, `blocking_session_id`)? Query Store can force the old good plan as a quick fix while you find the cause.

**Q20. Clustered vs nonclustered index?**
A: Clustered = the table itself, sorted by the key; one per table. Nonclustered = a separate structure pointing to rows. For payments: clustered on a narrow, ever-increasing key; nonclustered `(MerchantId, CreatedAt) INCLUDE (Status, Amount)` for the common list query.

**Q21. Why is a GUID clustered key a problem?**
A: Random inserts → page splits and fragmentation. Use `NEWSEQUENTIALID()` or a `BIGINT IDENTITY` clustered key with the GUID as a unique nonclustered column.

**Q22. Table variable vs temp table?**
A: Temp tables have statistics and can be indexed; better for more than a few hundred rows. Table variables are fine for tiny sets. TVPs pass sets from the app (Lab 05).

## F. EF Core and concurrency

**Q23. Optimistic concurrency in EF Core?**
A: Mark a `rowversion` column with `[Timestamp]` (or `IsRowVersion()`). EF adds `WHERE RowVer = @original` to every update. 0 rows affected → `DbUpdateConcurrencyException`. Handle it: reload and retry, or return 409.

**Q24. Can EF Core do `UPDLOCK` or `READPAST`?**
A: Not with LINQ. Options: `FromSql("SELECT … WITH (UPDLOCK, ROWLOCK) …")`, a stored procedure via `ExecuteSqlAsync`, or a command interceptor. For queue-style claims I use a proc — it is the right tool.

**Q25. How do you call a stored procedure from EF Core?**
A: `await db.Database.ExecuteSqlAsync($"EXEC dbo.usp_CapturePayment {id}, {amount}")` — the interpolated values become parameters, not string concatenation. For results: `db.Database.SqlQuery<CaptureResult>($"EXEC …")` or `FromSql` on an entity.

**Q26. `ExecuteUpdateAsync` — and its concurrency trap?**
A: EF 7+ runs one set-based `UPDATE` without loading entities. It bypasses the change tracker, so tracked entities in the context go stale, and it does not apply the concurrency token check automatically — add the condition in the `Where` yourself.

**Q27. Transactions in EF Core?**
A: `SaveChanges` is already one transaction. For several saves or mixing with raw SQL: `await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);` … `await tx.CommitAsync();`. Add `EnableRetryOnFailure` for transient errors — but then user-started transactions need an execution strategy wrapper.


**Q28. ETag and `If-Match` — what problem do they solve that `rowversion` alone doesn't?**
A: `rowversion` catches a clash between two saves. `If-Match` catches the clash between what the client *saw* and what is there now. The client reads version 7, decides, and sends `If-Match: "7"`. If the row is now version 8, the server answers `412 Precondition Failed` and changes nothing. That is optimistic concurrency stretched across the HTTP round trip and the human (or agent) thinking time.
Trap: "Hash the body for the ETag?" → No. Use the row version. A hash costs the serialisation you wanted to save, and it changes when the format changes but the data does not.

**Q29. 409 or 412?**
A: `409 Conflict`: the domain says no ("already captured"). It would say no again on retry. `412 Precondition Failed`: your copy is stale. Read again, decide again, then retry. Different `code` values in the ProblemDetails, so a client or an agent knows which path to take.
Trap: "Why does this matter for an agent?" → An agent that read a payment 10 steps ago is working from an old copy. `If-Match` (or a refund token bound to the version) makes it act on what it saw or not at all.

## Aperio: have you solved this before?

**Q-APERIO. "Have you solved this before?"**
A: The ideas, yes; SQL Server, no. In Aperio I used a row lock (`SELECT … FOR UPDATE`) to fix a double-approval race, and a compare-and-swap `UPDATE` to claim an approved action exactly once. I have not written T-SQL procs, lock hints, RCSI or deadlock handling in Aperio. This lab is where I did that on SQL Server itself.
Details and file references: the "Aperio comparison" table in this lab's `README.md`.
