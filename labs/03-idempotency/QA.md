# Lab 03 — Interview Q&A: idempotency and concurrent retries

This is one of the highest-value labs. Payments interviewers almost always ask it.

---

**Q1. What is an idempotent operation?**
A: Doing it many times has the same effect as doing it once. `SET status = 'captured'` is idempotent; `balance = balance + 10` is not.

**Q2. Why can't the client just "not retry"?**
A: On a timeout the client does not know if the server got the request. If it doesn't retry, the payment may be lost. If it does, it may be doubled. Idempotency makes retry safe, so the client always retries.

**Q3. How does the `Idempotency-Key` header work?**
A: The client generates a unique key (UUID) per *logical* operation and sends it with every retry of that operation. The server stores the key with the result. A repeat with the same key returns the stored result instead of doing the work again.

**Q4. Describe your implementation.**
A: Table `idempotency_keys(merchant_id, key, request_hash, status, response_code, response_body, created_at)` with a **unique index on (merchant_id, key)**. Insert-first: try `INSERT` the key as `in_progress`. If it succeeds, I own it: do the work, store the response, mark `completed`. If the insert hits the unique violation, someone else owns it: read the row and return its stored response.

**Q5. Why insert-first and not "SELECT, then INSERT if missing"?**
A: Check-then-act is a race. Two requests both SELECT, both see nothing, both charge. The unique index makes the database the single arbiter; exactly one INSERT wins.

**Q6. What if the same key comes with a different body?**
A: Store a hash of the request. Same key + different hash → **422** (`idempotency_key_reused`). It is a client bug, and silently returning the old result would hide it.

**Q7. What if the second request arrives while the first is still running?**
A: The row exists with status `in_progress`. Return **409** with `Retry-After`, or wait briefly and poll. Never run the work a second time.

**Q8. What if the server crashes after charging but before saving the response?**
A: That is the hard case. Options: do the payment write and the key update in **one DB transaction** (works if the side effect is in the same DB). If the side effect is external (call to acquirer), store progress steps ("recovery points") and pass *your own* idempotency key downstream, so the retry resumes instead of repeating.

**Q9. Should you cache errors?**
A: Cache final business outcomes, including declines and 4xx validation errors — they will not change. Do **not** cache 5xx/transient failures; release the key (or mark it retryable) so the client's retry can actually try again.

**Q10. How long do you keep keys?**
A: Longer than any client retries — commonly 24 hours to a few days. A cleanup job deletes old rows. Document it.

**Q11. Why scope the key by merchant?**
A: Keys are client-generated. Two merchants can produce the same string. The unique index is `(merchant_id, key)`.

**Q12. How did you test it?**
A: Fire 50 identical POSTs in parallel (`Task.WhenAll`) with the same key. Assert: one payment row in the DB, all 50 responses carry the same payment id, and the statuses are 201 for the winner plus 201-replay or 409 for the rest — as specified.

**Q13. Is a distributed lock (Redis) a better way?**
A: Not needed when the DB unique constraint already serialises the decision. A lock adds another failure mode (lock expiry, split brain). Use the database's guarantees first.

**Q14. Idempotency vs deduplication?**
A: Deduplication drops repeated messages. Idempotency returns the *same result* for repeats. An API needs the second: the client must get the answer.

**Q15. How does this connect to AI agents?**
A: Agents retry a lot — on timeouts, on their own confusion, after a context trim. A tool that is not idempotent is dangerous in an agent loop. In Lab 08 `refund_payment` requires an idempotency key; a repeated call returns the first refund, never a second one.
Aperio link: the executor reuses the prior result for an identical in-turn tool call instead of re-executing it (`lib/tools/executor.js`, duplicate-call cache). Same idea at the agent layer.

**Q16. Isolation level for the insert-first pattern?**
A: Default READ COMMITTED is enough; the unique index does the work. Higher levels add locking without adding safety here.

**Q17. Postgres unique violation code? SQL Server?**
A: Postgres `23505`. SQL Server error `2627` (unique constraint) or `2601` (unique index). Catch those specifically, not every exception.

**Q18. Could you use `INSERT … ON CONFLICT DO NOTHING RETURNING`?**
A: Yes in Postgres: no exception, and zero rows returned tells you that you lost. SQL Server has no direct equivalent; use `MERGE` carefully (it has race issues without `HOLDLOCK`) or catch 2627.

**Q19. What about idempotency for capture and refund, not only create?**
A: Same header on every state-changing POST. Plus the state machine itself helps: capturing an already-captured payment for the same amount can be treated as a replay; a different amount is a conflict.

**Q20. What would you monitor?**
A: Replay rate, 409 in-progress rate, key reuse with different body (client bugs), table size, cleanup lag.

## From the build (added 2026-10-20)

**Q26. Why two phases, when one transaction would make the losers simply wait?**
A: One transaction works only while the side effect lives in the same database. The moment the work is a call to an acquirer, you cannot hold a database transaction open across it, so the key must be claimed and committed first. Learning the two-phase shape on the easy case means lab 16 (in-doubt calls) is a small step, not a rewrite.
Trap: "then the in_progress state is visible." Yes — that is the point. The client sees `409` + `Retry-After` and retries; it never gets a second charge.

**Q27. Your replay says "Created", but the payment is now "Authorised". Bug?**
A: No. A replay returns the answer the first request got, byte for byte. It is not a fresh read. A client that wants the current state calls `GET`.

**Q28. Why is a `404` not stored, but a `422` is?**
A: The `404` is decided before the claim, and it may stop being true — the payment can exist a second later. A `422` from the state machine is a final answer to that exact request. Store final business answers; never store "not found yet" or a `5xx`.

**Q29. Where does the "key completed" update run, and why there?**
A: Inside the payment write's own transaction, on the same connection, as a conditional update (`… where status = 'InProgress'`) that must change exactly one row. So the payment and its stored answer commit together or not at all. Two transactions would leave a window where the payment exists and the key still says in progress — the client then gets `409` forever.

**Q30. A request crashes between phase 1 and phase 2. What happens to its key?**
A: A handled exception releases it: the row is deleted, so the retry can really try again. A hard crash (process killed) leaves it `InProgress`, and nothing reclaims it in this lab. A real system stores a `locked_at` time and lets a stale claim be taken over after a timeout. That is `CF-026`, and lab 16's topic.

**Q31. What is in your request hash, and why the path?**
A: Method, path and the bound request record, through SHA-256. The path holds the payment id and the command. Without it, a key used for a capture and then for a refund would replay the capture's `200` for a refund that never ran.

## EF Core version

Not built in this lab (decided 2026-10-20). Answer these from the sketch in `docs/aperio/AGENT-READY-BACKEND.md` §5, and say why: the unique key is the lesson, and Dapper showed it in full.

**Q21. Show idempotency in EF Core.**
A: Check for the key with `AsNoTracking`; if found, return it. Otherwise `Add` and `SaveChangesAsync`. Wrap the save in `catch (DbUpdateException ex) when (IsUniqueViolation(ex))` — a race lost to another request — then `ChangeTracker.Clear()` and read the winner. Sketch: `AGENT-READY-BACKEND.md` §5.

**Q22. Why is the `if` not enough on its own?**
A: Two threads can both pass the check before either inserts. Only the unique index is actually safe; the `if` is just a fast path for the common replay.

**Q23. Why `ChangeTracker.Clear()` in the catch?**
A: The failed entity is still tracked as `Added`. Any later `SaveChanges` on the same context would try the bad insert again. Clear it, or use a fresh context.

**Q24. How do you detect a unique violation from `DbUpdateException`?**
A: Look at `InnerException`: `SqlException` with `Number` 2627 or 2601 (SQL Server), `PostgresException` with `SqlState` "23505" (Postgres). Never match on message text.

**Q25. EF vs Dapper version — which did you prefer?**
A: (After the lab.) Typical: Dapper insert-first with `ON CONFLICT DO NOTHING RETURNING` is shorter and has no exception path; EF is what most existing codebases have, so you must know the catch pattern.


## Aperio: have you solved this before?

**Q-APERIO. "Have you solved this before?"**
A: Yes, in three forms. Duplicate tool calls in one turn reuse the first result. An approved action is claimed with a conditional update, so it runs once. And I fixed a real check-then-act race in `approvePending()`, where two approvals could both pass the pending check — fixed with a row lock and a conditional update. What Aperio doesn't have is a client-supplied `Idempotency-Key`, and that is what this lab adds.
Details and file references: the "Aperio comparison" table in this lab's `README.md`.
