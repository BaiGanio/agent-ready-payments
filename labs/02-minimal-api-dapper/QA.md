# Lab 02 — Interview Q&A: Minimal API, Dapper, deterministic contracts

---

## A. Deterministic API design

**Q1. What does "deterministic interface" mean for a backend service?**
A: Same input + same state → same output, every time. Concretely: explicit typed request/response contracts, no hidden server state (no session, no "current payment"), stable error codes, no behaviour that depends on wall-clock or ordering unless it is in the input.

**Q2. Why does that matter more when an AI agent is the caller?**
A: An agent reads your schema and your error text to decide what to do next. Vague errors make it guess. Hidden state makes its plan wrong. A deterministic API lets the agent recover from errors by reading a code, not by trial and error.

**Q3. How do you return errors?**
A: RFC 9457 **ProblemDetails** (`application/problem+json`) with a stable `type` URI and a machine code extension, e.g. `"code": "amount_exceeds_captured"`. Status codes: 400 bad shape, 404 unknown id, 409 illegal state transition, 422 business rule failed.
Trap: "409 or 422 for refund too large?" → Pick one rule and document it. Mine: **409 is "not now"** (the state forbids it, the same body may work later), **422 is "not like that"** (the shape is fine, the number is wrong). A refund that is too large is 422. Consistency matters more than the choice. In my lab the `type` is a URN — `urn:payments-labs:error:amount_exceeds_captured` — because RFC 9457 wants a URI but a made-up `https://` domain that serves nothing is worse than saying so.

**Q4. Why not return exceptions' messages?**
A: They leak internals, change between versions and are not stable. The `code` is the contract; the `detail` is for humans.

**Q5. How do you version the API?**
A: URL (`/v1/...`) or a header. Never break a contract in place: add fields, don't rename or remove. Clients — and agents' cached tool schemas — depend on it.

**Q6. Minimal API vs controllers?**
A: Minimal APIs: less ceremony, faster, good for small focused services. Controllers: filters, conventions, large teams with established structure. Both run on the same pipeline. For a wrapper over an existing system, minimal APIs keep the surface obvious.

**Q7. How do you validate input?**
A: At the edge: shape and format (required, positive amount, ISO currency). Business rules stay in the domain. Validation errors return 400 with field names.

**Q8. What makes a `GET` safe and `PUT` idempotent?**
A: `GET` must not change state. `PUT` / `DELETE` must give the same result when repeated. `POST` is neither by default — that is why Lab 03 adds an `Idempotency-Key`.

## B. Dapper

**Q9. Why Dapper and not EF Core here?**
A: The job is a wrapper over an existing system with existing tables and stored procedures. Dapper maps raw SQL to objects with almost no overhead and no change tracking. I see and own every query. EF Core shines when the model owns the schema.

**Q10. How does Dapper avoid SQL injection?**
A: Parameters: `conn.QueryAsync<T>(sql, new { Id = id })`. Values go as `DbParameter`s, never concatenated into the SQL text.
Trap: "What about `IN` lists?" → Dapper expands `WHERE id IN @Ids` into parameters. For big lists use a TVP (SQL Server) or `= ANY(@Ids)` (Postgres).

**Q11. What does Dapper actually do?**
A: It is extension methods on `IDbConnection`. It builds a command, adds parameters, runs it, and maps reader columns to properties using cached generated IL. Under it is plain ADO.NET (Lab 05).

**Q12. Connection lifetime?**
A: Open late, close early: `await using var conn = new NpgsqlConnection(cs)`. The driver's **connection pool** makes that cheap. Never hold a connection in a singleton.

**Q13. How do you run several statements in one transaction with Dapper?**
A: `using var tx = await conn.BeginTransactionAsync();` and pass `transaction: tx` to every call, then `tx.CommitAsync()`.

**Q14. `Query` vs `QueryFirst` vs `QuerySingle`?**
A: `Query` = list. `QueryFirst` = first row, error if none. `QuerySingle` = exactly one row, error if 0 or >1. `…OrDefault` variants return default instead of error on zero rows. Use `Single` when "more than one" would be a bug.

**Q15. Async all the way — why?**
A: DB calls are I/O. `async` frees the thread while waiting, so the server handles more requests with fewer threads. Blocking on `.Result` can starve the thread pool and, in old ASP.NET with a sync context, deadlock.

## C. Testing the API

**Q16. Unit or integration tests for a Dapper repository?**
A: Integration, against a real Postgres (Testcontainers). Mocking `IDbConnection` tests nothing useful; the SQL is the logic.

**Q17. `WebApplicationFactory` — what is it?**
A: Boots the real API in memory for tests. You call it with `HttpClient`, so you test routing, binding, serialization and ProblemDetails for real.

**Q18. How do you keep integration tests independent?**
A: Each test creates its own data with unique ids, or each test class gets a fresh schema/transaction. Never depend on order.

**Q19. How do you structure the project — layers?**
A: Thin: endpoint → application handler → domain (Lab 01) → repository (Dapper). Dependencies point inward. The domain does not know Dapper exists.

**Q20. What would you log for each payment request?**
A: Correlation id, merchant id, payment id, operation, result code, duration. Never PAN or CVV. Structured logging (Serilog / `ILogger` with message templates), so logs are queryable.

## D. EF Core as the alternative

**Q21. Dapper or EF Core — how do you choose?**
A: EF Core when the app owns the schema and saves whole aggregates with change tracking, migrations and LINQ. Dapper (or ADO.NET) when the schema already exists, stored procedures own the logic, on hot read paths and for bulk work. A wrapper over an existing payment database is the second case. Many teams use both: EF for app-owned tables, Dapper for the heavy queries.

**Q22. Can EF Core and Dapper share a connection and a transaction?**
A: Yes. `var conn = db.Database.GetDbConnection();` and `db.Database.CurrentTransaction?.GetDbTransaction()` — pass both to Dapper. One transaction, two tools.

**Q23. What makes EF Core slow when people complain about it?**
A: Tracking queries on read-only paths (use `AsNoTracking`), N+1 lazy loading, loading whole entities instead of projecting with `Select`, cartesian explosion from many `Include`s (use `AsSplitQuery`), and calling `SaveChanges` in a loop.

**Q24. Compiled queries, `FromSql`, `SqlQuery<T>`?**
A: `EF.CompileAsyncQuery` removes query-translation cost on hot paths. `FromSql` runs raw SQL that maps to an entity and can still be composed with LINQ. `Database.SqlQuery<T>` (EF 8+) maps raw SQL to any type — Dapper-like inside EF.

**Q25. What did running the same tests against both implementations show?**
A: (After the lab.) That the contract is in the interface and the tests, not the ORM. Same story as Aperio's two store backends behind one interface.

## E. API shape: CQRS-lite, MediatR, validation

**Q26. Minimal API or controllers for this service — and why?**
A: Minimal API. A small, focused service; less ceremony; endpoint groups and filters cover what controllers' attributes did. For a big existing controller codebase, I'd follow its style.

**Q27. What is CQRS?**
A: Command Query Responsibility Segregation: the model that *changes* data is separate from the model that *reads* it. Commands go through the domain and enforce rules. Queries return DTOs shaped for the screen or tool, with no domain logic.

**Q28. What is "CQRS-lite" in your lab?**
A: Same database, two code paths. Commands → handler → `Payment` aggregate → repository. Queries → Dapper → DTO. No separate read store, no events.
Trap: "When would you go full CQRS with a separate read store?" → When reads and writes have very different scale or shape — e.g. a reporting/search side fed by events. It adds eventual consistency, so the UI and the agent must accept slightly stale reads.

**Q29. Why no MediatR?**
A: It adds an indirection layer (send → find handler) that you don't need when you can inject the handler directly. Its real value is pipeline behaviours (logging, validation, transactions) — and endpoint filters or decorators do that too. Also, MediatR moved to a commercial licence in 2025, so many teams are re-checking it. I know it well enough to work in a codebase that uses it.

**Q30. Where does validation live?**
A: Two layers. Shape at the edge (required, format, ranges) — FluentValidation or built-in validation in an endpoint filter, returning 400 with field errors. Business rules in the domain (refund ≤ captured), returning 409/422 with a `code`.

**Q31. FluentValidation vs DataAnnotations?**
A: DataAnnotations: attributes on the DTO, fine for simple rules (.NET 10 Minimal APIs validate them out of the box). FluentValidation: rules in a separate class, easy conditional and cross-field rules, easy to unit test. I started with built-in and moved to FluentValidation when rules got conditional.

**Q32. Should a validator call the database (e.g. "merchant exists")?**
A: Prefer not. Validators check shape; existence and state checks belong in the handler, inside the same transaction, or you get race conditions between "validated" and "used".


**Q33. Offset or keyset paging?**
A: Keyset for a newest-first feed. Offset gets slower the deeper you go (the database still walks the skipped rows), and it skips or repeats rows when new ones arrive between two page calls. Keyset costs the same on every page and does not skip, but you cannot jump to page 7. I use `ORDER BY created_at DESC, id DESC` with `WHERE (created_at, id) < (cursor)`.
Trap: "Is a keyset page a snapshot?" → No. It walks a moving list in a stable direction. A row that changes while you page shows its new state.

**Q34. Why add `id` to the `ORDER BY`?**
A: To make the order total (no ties ever). Two payments from the same millisecond can come back in either order. SQL makes no promise about ties, so pages would overlap or miss rows, and order tests would be flaky. `id` is unique, so `(created_at, id)` has no ties.
Trap: "Which index?" → One on `(merchant_id, created_at DESC, id DESC)`, so the seek and the sort come from the index.

## Aperio: have you solved this before?

**Q-APERIO. "Have you solved this before?"**
A: Partly. Aperio has typed tool schemas, real status codes, and two engines behind one store interface, which is the same shape as my Dapper/EF stretch here. Its HTTP errors are a consistent `{ error }` object, but not ProblemDetails with a stable code. That is exactly the gap this lab closes, and I'd say so.
Details and file references: the "Aperio comparison" table in this lab's `README.md`.
