# Progress

The status board. One row per lab. This is the only place that says where you are.

Updated 2026-10-23 (**lab 09 closed**: the agent loop, the guards, crash and resume, two agents on one payment, and the evaluation runner with its ten scenarios. 385 tests green, 369 in CI, 0 warnings. Closed with five rows carried out: `CF-044` to `CF-048`). Two models are scored: rung 1 at 5 of 10, `Ornith-1.0-9B` at 8 of 10.

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
- The lab **in flight** gets a tick list of its own, below the board (`plan/PLAN.md` §8).
- When a lab closes, that tick list is deleted and the lab gets one line under [Finished labs](#finished-labs). The git tag is the record.
- A lab may close with work carried out of it. Every such item is a `CF-NNN` row and is named in **What is left**, never dropped quietly. Lab 09 closed that way: `CF-044` to `CF-048`.

## Next up

**Lab 04 is next.** Its bench sheet is already updated for the new order: its script is `db/sqlserver/0002_payments_procs_locking.sql`, and its step 3 is a check, because lab 09 did `CF-016`. Start with its [`WORKBENCH.md`](../labs/04-sqlserver-procs-locking/WORKBENCH.md) step 0. It closes `CF-009` on the way.

**The double refund is fixed and on `main`.** Two refunds with different `Idempotency-Key`s both won, 29 of 30 rounds (measured 2026-10-22). Lab 09 steps 4–5 fixed it — `select … for update`, the operation re-count and one retry (`plan/PLAN.md` §8) — and it landed on `main` with the `lab-09` tag.

**Five rows carried out of lab 09:** the local model is not picked (`CF-044`, one `eval` run and a decision), `DEMO.md` was never rehearsed (`CF-045`), lab 09's `QA.md` was not said out loud (`CF-046`), both model scores predate a check correction and need one re-run each (`CF-047`), and scenario S02 needs a judgement — both models repair a comma amount before the tool, so the money fence never fires (`CF-048`).

**The CI gate (`CF-006`) waits until lab 04 closes** (decided 2026-10-22). `.github/workflows/ci.yml` exists and its command is verified on this Mac. What is left after lab 04: push `develop`, so GitHub runs the workflow once and learns the check exists, then turn on branch protection for `main` with `build-and-test` required.

The suite is **385 of 385 green** (369 with the CI filter), on a build with **0 warnings**, measured 2026-10-23.

### Lab 04 — tick list

Steps are in the lab's `WORKBENCH.md`. Tick each one in the same commit as the step.

Copied back when lab 04 starts, from its `WORKBENCH.md` steps.

## The board

| # | Lab | Status | Tag | What is left |
|---|---|---|---|---|
| 00 | [Setup + glossary](../labs/00-setup-and-glossary) | **Done** | `lab-00` | — |
| 01 | [Payment state machine (TDD)](../labs/01-payment-state-machine) | **Done** | `lab-01` | — |
| 02 | [Minimal API + Dapper](../labs/02-minimal-api-dapper) | **Done** | `lab-02` | Closed short of the spec: paging `CF-022`, the EF Core store `CF-023`, `void` `CF-025` |
| 03 | [Idempotency](../labs/03-idempotency) | **Done** | `lab-03` | — |
| 04 | [SQL Server procs + locking](../labs/04-sqlserver-procs-locking) | **Prepared** | — | **Next up.** Closes `CF-009` on the way (`CF-016` was done in lab 09) |
| 05 | [Bare ADO.NET](../labs/05-bare-adonet) | Spec only | — | Bench sheet + test list, then the lab |
| 06 | [Flow pipeline (Channels)](../labs/06-flow-pipeline-channels) | Spec only | — | Bench sheet + test list, then the lab |
| 07 | [BDD refund rules](../labs/07-bdd-refund-rules) | Spec only | — | Decide how deep BDD goes (`PLAN.md` §7), then the lab |
| 08 | [MCP server](../labs/08-mcp-server) | **Done** | `lab-08` | `release/x.y` is cut after this lab, once the CI gate (`CF-006`) is pushed and `main` is protected |
| 09 | [Agent loop](../labs/09-agent-loop) | **Done** | `lab-09` | The model pick `CF-044`, the demo rehearsal `CF-045`, `QA.md` out loud `CF-046`, two re-scores `CF-047`, the S02 question `CF-048` |
| 10 | [Framework → Core (paper)](../labs/10-framework-to-core) | Spec only | — | Migration-note scaffold, then the lab |
| 11 | [k3s on the Pi](../labs/11-k3s-pi) *(optional)* | Spec only | — | Bench sheet, then the lab |
| 12 | [Semantic search](../labs/12-semantic-search) *(optional)* | Spec only | — | Bench sheet + test list, then the lab |
| 13 | [Chargeback workflow](../labs/13-chargeback-workflow) | Spec only | — | Bench sheet + test list, then the lab |
| 14 | [Observability + audit](../labs/14-observability-audit) | Spec only | — | Bench sheet + test list, then the lab. Owns `CF-017` |
| 15 | [Context + caching](../labs/15-context-and-caching) | Spec only | — | Bench sheet, then the lab |
| 16 | [Failures + recovery](../labs/16-failure-recovery-outcomes) | Spec only | — | Bench sheet + test list, then the lab. Owns `CF-026` and `CF-029` |
| 17 | [Cold start](../labs/17-cold-start) | Spec only | — | Bench sheet, then the lab. Owns `CF-004` |
| 18 | [System design capstone](../labs/18-system-design-capstone) | Spec only | — | Design-doc scaffold, then the lab |
| 19 | [Human-in-the-loop UI](../labs/19-human-in-the-loop-ui) *(optional)* | Spec only | — | Bench sheet, then the lab |
| 20 | [ETag + If-Match](../labs/20-etag-if-match) *(optional)* | Spec only | — | Moved out of lab 04 on 2026-10-22. Bench sheet + test list, then the lab |
| 21 | [Engine parity](../labs/21-engine-parity) | Spec only | — | Added 2026-10-23. Runs after lab 06. Bench sheet + test list, then the lab |

Every lab from 02 on needs three files before it can start: `WORKBENCH.md`, the test list, and any scaffold. The agent writes them **at the start of that lab**, never earlier (`plan/PLAN.md` §8).

## Finished labs

One line each. The lab's own `README.md`, `WORKBENCH.md` and `QA.md` hold the detail; the tag holds the code.

| Lab | Tag | Shipped | Left undone, on purpose |
|---|---|---|---|
| 00 Setup + glossary | `lab-00` | Docker compose (Postgres arm64 + SQL Server under Rosetta), `src/Payments.Db` DbUp runner, both baselines, `develop`, `GLOSSARY.md` in your own words | — |
| 01 State machine | `lab-01` | `src/Payments.Domain` — `Money`, `Result`, the enums, `PaymentOperation`, `Payment`. 160 tests green. `global.json` SDK pin + Microsoft.Testing.Platform | The step 12 refactor onto one transition table: `CF-013` |
| 02 Minimal API + Dapper | `lab-02` | `src/Payments.Api` — `ProblemMapper`, contracts, the Dapper store, `POST /payments` and `GET /payments/{id}`. Testcontainers + DbUp + Respawn harness. `SteppingClock` and the four `StoreTests` | Paging `CF-022`, EF Core store `CF-023`, `void` `CF-025`. `updatedAt` on create still has two clocks: `CF-019` |
| 03 Idempotency | `lab-03` | `db/postgres/0002_idempotency.sql`, `DapperIdempotencyStore` (insert-first claim), `ReadKey` / `Replay` / `CommandAsync` in `PaymentEndpoints.cs`. All four POSTs replay-safe. **Completed 2026-10-22:** the 3 `ConcurrencyTests` (`CF-035`) and the crash path on update (`CF-036`), both seen red first | — |
| 09 Agent loop | `lab-09` | `src/Payments.Migrations` (`CF-016`), `tests/Payments.Testing` (`CF-033`), the double-refund fix with its race tests, `db/sqlserver/0001_agent_run.sql`, `AgentDbContext` + `EfAgentRunStore`, `AgentLoop` with all ten guards (`CF-007`), `McpToolHost` that cuts the token out, `IApprovalChannel`, the console (`chat`, `run`, `resume`, `show`, `seed-demo`, `eval`), two configured agents, crash and resume, two agents on one payment, and the evaluation runner with ten scenarios and a saved report per run. 385 tests green, 0 warnings | The model pick: rung 1 scored 5 of 10 and `Ornith-1.0-9B` 8 of 10, but rung 3 was never loaded, so `PLAN.md` §7 "Local model" stays open (`CF-044`, re-scores `CF-047`). `DEMO.md` written but not rehearsed (`CF-045`). `QA.md` not said out loud (`CF-046`). S02's comma amount needs a judgement (`CF-048`) |
| 08 MCP server | `lab-08` | `api_key` table, hashed keys and scopes, one merchant per key, the capped list, `Payments.Mcp` with 3 tools and the two-phase refund, the token store. MCP Inspector by hand. **`CF-031` closed 2026-10-22:** a `401` is its own branch with a stop hint | Two repeated MCP harnesses: `CF-033`, judged not worth sharing until lab 09 |

Decisions made in these labs live in `plan/PLAN.md` §8, dated.
