# Progress

The status board. One row per lab. This is the only place that says where you are.

Updated 2026-10-18.

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
| 01 | [Payment state machine (TDD)](../labs/01-payment-state-machine) | **Prepared** | — | Write `src/Payments.Domain` until the 160 tests are green |
| 02 | [Minimal API + Dapper](../labs/02-minimal-api-dapper) | Spec only | — | Bench sheet + test list, then the lab |
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

## Lab 01 — Prepared

Written for you:

- [x] `README.md`: spec, state diagram, the 40-case transition table, the public shape, 9 hard requirements
- [x] `WORKBENCH.md`: 17 steps, target-state diagram, 13 traps
- [x] `TDD-NOTES.md`: scaffold, empty on purpose
- [x] `tests/Payments.Domain.Tests`: 160 tests, red (xUnit v3, decided 2026-10-18)

Left for you:

- [ ] `global.json`: SDK pin + the Microsoft.Testing.Platform opt-in
- [ ] `src/Payments.Domain`: `Money`, `Result`, the enums, `PaymentOperation`, `Payment`
- [x] 160 tests green (the Step 12 refactor onto one transition table is carried forward as CF-013)
- [ ] The four purity greps in `README.md` → "Done when"
- [ ] `TDD-NOTES.md` filled in
- [ ] `QA.md` answered out loud, misses marked
- [ ] Close the lab: merge, tag `lab-01`, back-merge `main` into `develop`, delete the branch

Nothing is blocked. The test framework is decided: **xUnit v3** (`plan/PLAN.md` §8).
