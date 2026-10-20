# Progress

The status board. One row per lab. This is the only place that says where you are.

Updated 2026-10-20.

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
| 03 | [Idempotency](../labs/03-idempotency) | Spec only | — | Bench sheet + test list, then the lab |
| 04 | [SQL Server procs + locking](../labs/04-sqlserver-procs-locking) | Spec only | — | Bench sheet + test list, then the lab |
| 05 | [Bare ADO.NET](../labs/05-bare-adonet) | Spec only | — | Bench sheet + test list, then the lab |
| 06 | [Flow pipeline (Channels)](../labs/06-flow-pipeline-channels) | Spec only | — | Bench sheet + test list, then the lab |
| 07 | [BDD refund rules](../labs/07-bdd-refund-rules) | Spec only | — | Decide how deep BDD goes (`PLAN.md` §7), then the lab |
| 08 | [MCP server](../labs/08-mcp-server) | Spec only | — | Bench sheet + test list, then the lab. `release/x.y` is cut after this lab |
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
