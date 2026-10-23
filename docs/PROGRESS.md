# Progress

The status board. One row per lab. This is the only place that says where you are.

Updated 2026-10-23 (lab 09 steps 14–15 done: the model never holds the refund token, only a human yes confirms, the ceiling is a host rule. 375 tests green — the **stop mark**).

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

## Next up

**Lab 09 is next** (decided 2026-10-22: it runs before lab 04). Prepared 2026-10-22: spec, bench sheet, test list, evaluation-set scaffold and demo runbook are written. Start with its [`WORKBENCH.md`](../labs/09-agent-loop/WORKBENCH.md) step 0.

**Lab 04 comes after lab 09.** Its bench sheet is already updated for the new order: its script is now `0002`, and its step 3 is a check, because lab 09 does `CF-016`.

**A real bug is still open on `main`:** two refunds with different `Idempotency-Key`s can both win (29 of 30 rounds, measured 2026-10-22). **Fixed 2026-10-23 on `feature/lab-09-agent-loop`** by steps 4–5 — `select … for update`, the operation re-count and one retry (`plan/PLAN.md` §8). It reaches `main` when the lab merges.

**The CI gate (`CF-006`) waits until lab 04 closes** (decided 2026-10-22). `.github/workflows/ci.yml` exists and its command is verified on this Mac. What is left after lab 04: push `develop`, so GitHub runs the workflow once and learns the check exists, then turn on branch protection for `main` with `build-and-test` required.

The suite is **330 of 330 green**, measured 10 runs out of 10, on 2026-10-22.

### Lab 09 — tick list

Steps are in the lab's `WORKBENCH.md`. Tick each one in the same commit as the step.

- [x] 1 Start the lab, commit the prep
- [x] 2 `Payments.Migrations` (`CF-016`)
- [x] 3 `Payments.Testing` (`CF-033`)
- [x] 4–5 The double refund: red, then lock, check, retry once
- [x] 6–7 Agent projects, `0001_agent_run.sql`
- [x] 8–9 EF Core run store
- [x] 10–11 The loop
- [x] 12–13 The guards (`CF-007` closed)
- [x] 14–15 The token, the human, the ceiling — **stop mark**
- [x] 16 The console against `llama-server`
- [ ] 17 Configured agents, `chat`, demo data
- [ ] 18–19 Crash and resume
- [ ] 20 Two agents, one payment
- [ ] 21–23 Evaluation set, rung 1, rung 3, the pick
- [ ] 24 Rehearse the demo (`DEMO.md`)
- [ ] 25 Done-when list
- [ ] 26 Answers out loud
- [ ] 27 Close

## The board

| # | Lab | Status | Tag | What is left |
|---|---|---|---|---|
| 00 | [Setup + glossary](../labs/00-setup-and-glossary) | **Done** | `lab-00` | — |
| 01 | [Payment state machine (TDD)](../labs/01-payment-state-machine) | **Done** | `lab-01` | — |
| 02 | [Minimal API + Dapper](../labs/02-minimal-api-dapper) | **Done** | `lab-02` | Closed short of the spec: paging `CF-022`, the EF Core store `CF-023`, `void` `CF-025` |
| 03 | [Idempotency](../labs/03-idempotency) | **Done** | `lab-03` | — |
| 04 | [SQL Server procs + locking](../labs/04-sqlserver-procs-locking) | **Prepared** | — | After lab 09. Closes `CF-009` on the way (`CF-016` moved to lab 09) |
| 05 | [Bare ADO.NET](../labs/05-bare-adonet) | Spec only | — | Bench sheet + test list, then the lab |
| 06 | [Flow pipeline (Channels)](../labs/06-flow-pipeline-channels) | Spec only | — | Bench sheet + test list, then the lab |
| 07 | [BDD refund rules](../labs/07-bdd-refund-rules) | Spec only | — | Decide how deep BDD goes (`PLAN.md` §7), then the lab |
| 08 | [MCP server](../labs/08-mcp-server) | **Done** | `lab-08` | `release/x.y` is cut after this lab, once the CI gate (`CF-006`) is pushed and `main` is protected |
| 09 | [Agent loop](../labs/09-agent-loop) | **Prepared** | — | The lab, next. Fixes the double refund, closes `CF-016`, `CF-033`, `CF-007`; picks the local model; live demo |
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
| 08 MCP server | `lab-08` | `api_key` table, hashed keys and scopes, one merchant per key, the capped list, `Payments.Mcp` with 3 tools and the two-phase refund, the token store. MCP Inspector by hand. **`CF-031` closed 2026-10-22:** a `401` is its own branch with a stop hint | Two repeated MCP harnesses: `CF-033`, judged not worth sharing until lab 09 |

Decisions made in these labs live in `plan/PLAN.md` §8, dated.
