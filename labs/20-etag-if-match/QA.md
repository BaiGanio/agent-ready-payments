# Lab 20 — Interview Q&A: ETag, If-Match, stale reads

Q1 and Q2 moved here from lab 04 (they were Q28 and Q29 there) on 2026-10-22.

---

**Q1. ETag and `If-Match` — what problem do they solve that `rowversion` alone doesn't?**
A: `rowversion` catches a clash between two saves. `If-Match` catches the clash between what the client *saw* and what is there now. The client reads version 7, decides, and sends `If-Match: "7"`. If the row is now version 8, the server answers `412 Precondition Failed` and changes nothing. That is optimistic concurrency stretched across the HTTP round trip and the human (or agent) thinking time.
Trap: "Hash the body for the ETag?" → No. Use the row version. A hash costs the serialisation you wanted to save, and it changes when the format changes but the data does not.

**Q2. 409 or 412?**
A: `409 Conflict`: the domain says no ("already captured"). It would say no again on retry. `412 Precondition Failed`: your copy is stale. Read again, decide again, then retry. Different `code` values in the ProblemDetails, so a client or an agent knows which path to take.
Trap: "Why does this matter for an agent?" → An agent that read a payment 10 steps ago is working from an old copy. `If-Match` (or a refund token bound to the version) makes it act on what it saw or not at all.

**Q3. What does `If-None-Match` do on a `GET`?**
A: The client sends the ETag it already has. If the resource has not changed, the server answers `304 Not Modified` with no body, and the client uses its copy. It saves bandwidth and serialisation, not a database read — the server still has to know the current version.
Trap: "So 304 makes the endpoint fast?" → Only the response is smaller. The version lookup still runs; make it a narrow query.

**Q4. Strong or weak ETag?**
A: A strong ETag (`"7"`) means byte-for-byte the same. A weak one (`W/"7"`) means "the same meaning". `If-Match` uses the strong comparison, so for a write precondition use a strong ETag.

**Q5. Where does the version come from in Postgres, and why not `xmin`?**
A: An explicit `version int` column, set to `version + 1` in the same `UPDATE` that changes the row, with `where version = @expected`. `xmin` is the id of the transaction that wrote the row: it is Postgres only, it changes on a `VACUUM FREEZE`-style rewrite in ways you do not control, and it can wrap around. An explicit column also works the same way on SQL Server next to `rowversion`.

**Q6. Should `If-Match` be required on a refund?**
A: For an agent, yes: `428 Precondition Required` when it is missing. It forces "read, then act on what you read". For old human clients you may start optional and log the calls without it, then make it required.

**Q7. How does `If-Match` meet `Idempotency-Key`?**
A: They answer different questions. The key says "this is the same request again". `If-Match` says "only if nothing changed since I looked". A retry of a request that already succeeded must replay its answer, not fail with `412` because its own write bumped the version — so the idempotency check runs **first**.
Trap: "Then why put `If-Match` in the request hash?" → So the same key with a different precondition is a reused key (`422`), not a silent replay of a different decision.

## Aperio: have you solved this before?

**Q-APERIO. "Have you solved this before?"**
A: Partly. In Aperio an approved agent action is claimed with a conditional `UPDATE … WHERE status IN ('approved','edited')`, so it runs once and only if it is still approved. That checks the **status**, not a **version**: if someone edited the target after the approval, Aperio would not notice. This lab adds the version, end to end over HTTP.
Details and file references: the "Aperio comparison" table in this lab's `README.md`.
