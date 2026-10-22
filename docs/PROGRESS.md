# Progress

The status board. One row per lab. This is the only place that says where you are.

Updated 2026-10-22.

## How to read it

| Status | Means |
|---|---|
| `Done` | Merged to `main`, tagged `lab-NN`, feature branch deleted, `QA.md` answered out loud |
| `In progress` | A `feature/lab-NN-…` branch exists and has commits |
| `Prepared` | The agent wrote the spec, the bench sheet and the test list. No code yet |
| `Spec only` | `README.md` (simple example + why) and `QA.md` exist. No bench sheet, no tests |

Rules:

- The row changes in the **same commit** as the work it describes.
- A lab becomes `Done` only in the "Close the lab" step of its `WORKBENCH.md`, after the Done-when list in its `README.md` passes.
- `What is left` is one line. If it needs a paragraph, it is a `CF-NNN` row in [`CARRY-FORWARD.md`](plan/CARRY-FORWARD.md) or a decision in `plan/PLAN.md` §8.
- Ticks in the detail sections below are the lab's own deliverables, not extra work.

## The board

| # | Lab | Status | Tag | What is left |
|---|---|---|---|---|
| 00 | [Setup + glossary](../labs/00-setup-and-glossary) | **Done** | `lab-00` | — |
| 01 | [Payment state machine (TDD)](../labs/01-payment-state-machine) | **Done** | `lab-01` | — |
| 02 | [Minimal API + Dapper](../labs/02-minimal-api-dapper) | **Done** | `lab-02` | Closed short of the spec: the store and its tests are done, the command endpoints are `CF-021` |
| 03 | [Idempotency](../labs/03-idempotency) | `In progress` | — | Steps 2–12 done plus `CF-032`: all four POSTs are replay-safe, 109 of 109 green. Step 13 skipped (`CF-035`). Steps 14–16 left: Done-when by hand, `QA.md` out loud, close the lab. Then the CI gate, `CF-006` |
| 04 | [SQL Server procs + locking](../labs/04-sqlserver-procs-locking) | Spec only | — | Bench sheet + test list, then the lab |
| 05 | [Bare ADO.NET](../labs/05-bare-adonet) | Spec only | — | Bench sheet + test list, then the lab |
| 06 | [Flow pipeline (Channels)](../labs/06-flow-pipeline-channels) | Spec only | — | Bench sheet + test list, then the lab |
| 07 | [BDD refund rules](../labs/07-bdd-refund-rules) | Spec only | — | Decide how deep BDD goes (`PLAN.md` §7), then the lab |
| 08 | [MCP server](../labs/08-mcp-server) | **Done** | `lab-08` | Closed short of the spec: step 19 needs lab 03's replay, so it is `CF-032`. `release/x.y` is cut after this lab, after the CI gate (`CF-006`) |
| 09 | [Agent loop](../labs/09-agent-loop) | Spec only | — | Pick the local model with the evaluation set, then the lab |
| 10 | [Framework → Core (paper)](../labs/10-framework-to-core) | Spec only | — | Migration-note scaffold, then the lab |
| 11 | [k3s on the Pi](../labs/11-k3s-pi) *(optional)* | Spec only | — | Bench sheet, then the lab |
| 12 | [Semantic search](../labs/12-semantic-search) *(optional)* | Spec only | — | Bench sheet + test list, then the lab |
| 13 | [Chargeback workflow](../labs/13-chargeback-workflow) | Spec only | — | Bench sheet + test list, then the lab |
| 14 | [Observability + audit](../labs/14-observability-audit) | Spec only | — | Bench sheet + test list, then the lab |
| 15 | [Context + caching](../labs/15-context-and-caching) | Spec only | — | Bench sheet, then the lab |
| 16 | [Failures + recovery](../labs/16-failure-recovery-outcomes) | Spec only | — | Bench sheet + test list, then the lab |
| 17 | [Cold start](../labs/17-cold-start) | Spec only | — | Bench sheet, then the lab. Owns `CF-004` |
| 18 | [System design capstone](../labs/18-system-design-capstone) | Spec only | — | Design-doc scaffold, then the lab |
| 19 | [Human-in-the-loop UI](../labs/19-human-in-the-loop-ui) *(optional)* | Spec only | — | Bench sheet, then the lab |

Every lab from 02 on needs three files before it can start: `WORKBENCH.md`, the test list, and any scaffold. The agent writes them **at the start of that lab**, never earlier (`plan/PLAN.md` §8).

## Lab 00 — Done, tag `lab-00`

- [x] `docker/docker-compose.yml`: Postgres (native arm64) + SQL Server (Rosetta), volumes, healthchecks
- [x] `.gitignore`, `.env.example`; `.env` ignored
- [x] `src/Payments.Db`: DbUp runner, `migrate` and `status`, both engines, exit 1 on failure
- [x] `db/postgres/0000_baseline.sql`, `db/sqlserver/0000_baseline.sql`
- [x] `develop` branch, `feature/lab-00-setup` merged with `--no-ff` and deleted
- [x] `GLOSSARY.md`, 28 words, in your own words
- [x] `QA.md` answered out loud

## Lab 01 — Done, tag `lab-01`

- [x] `global.json`: SDK pin + the Microsoft.Testing.Platform opt-in
- [x] `README.md`, `WORKBENCH.md` (17 steps), `TDD-NOTES.md`
- [x] `src/Payments.Domain`: `Money`, `Result`, the enums, `PaymentOperation`, `Payment`
- [x] 160 tests green
- [x] The four purity greps in `README.md` → "Done when"
- [x] `TDD-NOTES.md` filled in
- [x] `QA.md` answered out loud, misses marked
- [x] Merged, tagged `lab-01`, back-merged into `develop`, branch deleted

Left undone on purpose: the step 12 refactor onto one transition table. It is `CF-013` in the inbox, to be done in a later lab where a second aggregate shares the rule.

## Lab 02 — Done

Written for you (2026-10-19, step 13b added 2026-10-20):

- [x] `README.md`: the spec — decisions table, HTTP surface, error map, DTO, 12 hard requirements, Done-when
- [x] `WORKBENCH.md`: 23 steps (core 1–13b, stretch A 14–16, stretch B 17–19), 4 diagrams, 28 traps
- [x] `TEST-LIST.md`: 56 test names in 10 groups, with the rule each one must check
- [x] `STORE-NOTES.md`: scaffold, empty on purpose

Done by you:

- [x] Step 2: `Payment.Id` became a `Guid`; the 160 domain tests stayed green
- [x] Step 3: `db/postgres/0001_merchant_payment.sql`
- [x] Steps 4–8: `src/Payments.Api` — project, `ProblemMapper`, contracts, the Dapper store, create + get
- [x] Step 9: `tests/Payments.Api.Tests` harness — Testcontainers + DbUp + Respawn + `WebApplicationFactory`
- [x] Step 10: `CreatePaymentTests` and `GetPaymentTests`, plus the shared helpers in `ApiTestBase`
- [x] Step 13b: `SteppingClock`, `StoreFor`, the SQL helpers and all four `StoreTests`
- [x] `ReplayClock` takes a fallback clock — the bug step 13b found, `CF-024`, closed the same day

Yours to finish, by hand:

- [ ] Step 20: `QA.md` answered out loud, misses marked
- [ ] Step 21: merge, tag `lab-02`, back-merge `main` into `develop`, delete the branch

Dropped on purpose, each with a carry-forward row:

- `ValidationTests` → `CF-020`
- Steps 11–13, the five command endpoints and their four test groups → `CF-021`, picked up by lab 03
- Steps 14–16, stretch A paging → `CF-022`
- Steps 17–19, stretch B EF Core and `STORE-NOTES.md` → `CF-023`

Nothing is blocked. The four lab 02 decisions — merchant placement, the `Guid` id, replay loading and `snake_case` in Postgres — are in `plan/PLAN.md` §8, dated 2026-10-19. One finding is open against the shipped code: `CF-019`, `updatedAt` written by one clock and returned by another.

## Lab 03 — In progress

Written for you (2026-10-20):

- [x] `README.md`: the spec — 6 decisions, the header rules, the request flow, the key's life, the contracts, 4 new error codes, 11 hard requirements, Done-when
- [x] `WORKBENCH.md`: 17 steps (0–16), 2 diagrams, 21 traps
- [x] `TEST-LIST.md`: 65 test names in 12 groups, with the rule each one must check
- [x] `QA.md`: Q26–Q31 added from the build; the EF Core questions marked paper-only

Done by you (2026-10-20 → 2026-10-21, merged into `develop` as `82810f9`):

- [x] Step 2: `ValidationTests` — 8 tests, closes `CF-020`
- [x] Steps 3–4: `AuthoriseTests`, `CaptureTests`, `RefundTests` — 20 tests — then the three endpoints in `src/Payments.Api/PaymentEndpoints.cs`. Closes `CF-021`
- [x] Step 5: `db/postgres/0002_idempotency.sql` — the `idempotency_key` table
- [x] Step 6: `IIdempotencyStore`, `Claim`, `Completion` and `RequestHash`
- [x] Step 7: `IdempotencyStoreTests` — 7 tests, red

Done by you (2026-10-22, on `feature/lab-03-idempotency`):

- [x] Step 8: `DapperIdempotencyStore` — insert-first claim, the conditional `Completed` mark, release only when `InProgress`. The 7 step 7 tests are green
- [x] Step 9: `KeyHeaderTests`, `ReplayTests`, `KeyReuseTests` — red
- [x] Step 10: `ReadKey` and `Replay` in `PaymentEndpoints.cs`, the header check on all four POSTs, the two-phase flow on `POST /payments`, `ProblemMapper.Idempotency` and `IIdempotencyStore` registered. 96 of 98 green (see `CF-034`)
- [x] Step 11: `CommandReplayTests`, `InProgressTests`, `FailureTests` — 10 tests, 7 red
- [x] Step 12: `CommandAsync` — one two-phase flow shared by `authorise`, `capture` and `refund`, plus `ProblemMapper.Refusal` so a stored refusal is the same bytes as the live one. **108 of 108 green**
- [x] `CF-032`, lab 08's step 19: `A_dropped_answer_still_refunds_once` and the `DropFirstAnswer` handler. **109 of 109 green.** Seen red once with the `Claim.Replay` arm removed
- [ ] ~~Step 13: fifty at once~~ — **skipped 2026-10-22, `CF-035`.** No test covers the race; three `README.md` "Done when" lines are marked skipped

Yours to do:

- [ ] Steps 14–16: Done-when by hand, `QA.md` out loud, close the lab

The feature branch `feature/lab-03-idempotency` was merged into `develop` after step 7 and re-cut from `af34713`. It now carries steps 8–12 plus `CF-032`.

Decided for this lab (2026-10-20, `plan/PLAN.md` §8): own key table, two phases, the `Completed` mark in the payment write's transaction, no EF Core, no FluentValidation. The CI gate moved to after this lab.

## Lab 08 — Done (2026-10-22, tag `lab-08`)

Written for you (2026-10-21):

- [x] `README.md`: the spec — 10 decisions, scopes and keys, the key check, the capped list, the 3 tools, the two-phase refund, the token's life, 6 API and 16 tool error codes, 12 hard requirements, Done-when
- [x] `WORKBENCH.md`: 24 steps (0–23), 1 target-state diagram, 25 traps
- [x] `TEST-LIST.md`: 67 test names in 12 groups, with the rule each one must check
- [x] `QA.md`: Q29–Q38 added from the SDK checks; Q7, Q10, Q12, Q15, Q28 and Q-APERIO updated

Done by you:

- [x] Steps 2–6: `api_key` table, keys and scopes, one merchant per key, the capped list, tests first
- [x] Steps 7–13: `Payments.Mcp`, the harness, the fence, the read tools, tests first — 34 tests green
- [x] Steps 14–17: the token store and the two-phase refund, tests first
- [x] Step 18: end to end against the real API
- [ ] Step 19: the dropped-answer test — **not done**, it needs lab 03's replay. Carried forward as `CF-032`
- [x] Steps 20–23: the MCP Inspector by hand, Done-when, `QA.md` out loud, close the lab

Found by hand in step 20, after the tests were green:

- A stdio server that logs at `Information` freezes when the client stops reading stderr: the pipe fills and the next log line blocks. Fixed with `SetMinimumLevel(LogLevel.Warning)`. It is now a trap row in `WORKBENCH.md` and a checklist in `docs/EVERYDAY-COMMANDS.md`.
- A `401` reaches the model as `tool_failed` with the hint "try once more" — `CF-031`.

Decided for this lab (2026-10-21, `plan/PLAN.md` §8): audit row → lab 14, no paging (capped list), every call needs a key, hashed keys in a table, one merchant per key, tokens in the MCP server's memory, no cloud model in any demo.
