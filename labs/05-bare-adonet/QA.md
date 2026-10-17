# Lab 05 — Interview Q&A: bare ADO.NET, bulk load, streaming

---

**Q1. What are the core ADO.NET types?**
A: `SqlConnection`, `SqlCommand`, `SqlParameter`, `SqlDataReader`, `SqlTransaction`, `SqlBulkCopy`. (Old: `DataSet`/`DataTable`/`SqlDataAdapter` — disconnected, memory-heavy; still common in legacy .NET Framework code.)

**Q2. `System.Data.SqlClient` vs `Microsoft.Data.SqlClient`?**
A: `System.Data.SqlClient` is the old built-in provider, maintenance only. `Microsoft.Data.SqlClient` is the current one (new features, Azure AD auth, `Encrypt=true` default since v4). A Framework → Core move usually includes this switch.
Trap: "What broke when you switched?" → The `Encrypt=true` default can fail on servers with self-signed certs; you add `TrustServerCertificate=true` for dev only.

**Q3. How does connection pooling work?**
A: Pools are per **exact connection string**. `Open()` takes a connection from the pool; `Dispose()` returns it. Default max pool size is 100. If code leaks connections (no `using`), the pool empties and requests time out with "max pool size reached".

**Q4. Why stream with `SqlDataReader` instead of loading a list?**
A: Constant memory: one row at a time, forward-only. For 1M rows a `List<T>` holds them all; a reader with `IAsyncEnumerable<T>` and `yield return` holds one.

**Q5. What is `CommandBehavior.SequentialAccess`?**
A: Reads columns in order as a stream — for large `varbinary(max)`/`nvarchar(max)` values, so the whole row is not buffered. You must read columns in ordinal order.

**Q6. How did you load 1M rows fast?**
A: `SqlBulkCopy` with an `IDataReader` or `DataTable` source, `BatchSize` ~5,000–10,000, `TableLock` option for minimal logging into an empty heap, explicit `ColumnMappings`. Row-by-row `INSERT` is orders of magnitude slower because of a round trip per row.

**Q7. `SqlBulkCopy` trade-offs?**
A: Fires no triggers and checks no constraints by default (`FireTriggers`, `CheckConstraints` options exist). Errors are less precise. Common pattern: bulk load into a **staging table**, then one set-based `INSERT … SELECT`/`MERGE` inside a proc with validation.

**Q8. What is a TVP?**
A: Table-Valued Parameter: pass a whole set of rows to a proc as one parameter (`CREATE TYPE … AS TABLE`, `SqlDbType.Structured`). Good for hundreds to a few thousand rows; bulk copy for more.

**Q9. How does Dapper relate to this?**
A: Dapper = extension methods that do the `SqlCommand` + parameters + reader-to-object mapping for you, with cached IL for speed. The same pool, same driver, same `SqlDataReader` underneath. When I need bulk copy or full streaming control, I drop to ADO.NET on the same connection.

**Q10. Why set parameter types and sizes explicitly?**
A: `AddWithValue` infers types: a C# `string` becomes `nvarchar(<length of value>)`. Against a `varchar` column that causes implicit conversion (index scan instead of seek) and many plan variants (one per length). Use `Add("@Id", SqlDbType.VarChar, 50)`.

**Q11. Sync vs async ADO.NET?**
A: Async for request paths in a web server (frees threads). Note: `SqlDataReader` async reads of very large values were historically slow in `Microsoft.Data.SqlClient`; measure. For a CPU-bound batch job sync can be fine.

**Q12. `CommandTimeout` vs connection timeout?**
A: Connection timeout = how long to wait to open (default 15 s). `CommandTimeout` = how long a single command may run (default 30 s). Set `CommandTimeout` per command for known long batch work; never `0` (infinite) on a request path.

**Q13. Cancellation?**
A: Pass a `CancellationToken` to `ExecuteReaderAsync`/`ReadAsync`. The driver sends an attention signal to the server to stop the query.

**Q14. How do you handle transient errors?**
A: Retry only known transient error numbers (deadlock 1205, timeouts, failover errors) with backoff and a limit. `Microsoft.Data.SqlClient` has a configurable retry logic provider; Polly is the general option. Never retry a non-idempotent write blindly.

**Q15. Where is `DataTable` still useful?**
A: As a `SqlBulkCopy` or TVP source, and in legacy code. Not for new read paths.

**Q16. How do you read output parameters and return values?**
A: Add parameters with `Direction = Output` / `ReturnValue`. They are only populated **after the reader is fully consumed and closed**. A common bug.

**Q17. Multiple result sets?**
A: `reader.NextResult()` / `NextResultAsync()`. In Dapper: `QueryMultiple`.

**Q18. What did you measure?**
A: (After the lab.) Rows/second for row-by-row vs TVP vs bulk copy; peak memory for `List<T>` vs streaming. Quote your own numbers, and say they were on Rosetta-emulated SQL Server, so only the ratios matter.

**Q19. Bulk insert in EF Core?**
A: EF Core batches `SaveChanges` inserts (several rows per round trip), but it still tracks every entity and sends parameterised `INSERT`s. For 100k+ rows: `SqlBulkCopy` (or a third-party extension like EFCore.BulkExtensions). For set-based changes: `ExecuteUpdateAsync` / `ExecuteDeleteAsync`.

**Q20. What did your three-way comparison show?**
A: (After the lab.) Your numbers for `AddRange`+`SaveChanges` vs `ExecuteUpdateAsync` vs `SqlBulkCopy`. Say only the ratios matter, because SQL Server ran under Rosetta.


## Aperio: have you solved this before?

**Q-APERIO. "Have you solved this before?"**
A: Partly. Aperio talks to Postgres and SQLite through the raw drivers with hand-written SQL, and it has a batch insert. It doesn't stream million-row reads or do bulk copy, and it isn't .NET. This lab gives me ADO.NET and real numbers.
Details and file references: the "Aperio comparison" table in this lab's `README.md`.
