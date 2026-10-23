# Agent-Ready Payment Backend Labs

Hands-on labs for one question: **how do you build a .NET payment backend that AI agents can call safely?**

This is not a machine learning repo. No model is trained here. The focus is plain backend engineering — C#, SQL, concurrency, tests — done so that an AI agent can use the system as a set of reliable, deterministic tools.

## The goal

The labs prepare for a senior backend role of this shape:

- Core backend for a high-volume payment platform.
- C# on **.NET Framework** today, moving to **.NET Core / modern .NET**.
- **SQL Server stored procedures** for highly concurrent, multithreaded work.
- Design patterns and **flow-based processing**.
- **TDD and BDD**, Git with a Gitflow-style workflow.
- Services with **clear, deterministic interfaces** that work inside agentic workflows.
- A solid understanding of **AI agent orchestration**: agent loops, state, task execution, and tool-calling boundaries.

In short: strong backend fundamentals, plus knowing what changes when the caller is an agent and not a person.

## How the labs work

- One topic per lab, about 2–3 hours each.
- Each lab folder has a `README.md` (what we solve, a simple example, why it matters) and a `QA.md` (interview-style questions with answers).
- The lab gives the spec and failing tests. **You write the code.**
- Runnable code is **.NET 10**. Postgres for the API path, SQL Server (Docker) for stored procedures and locking.
- Agent labs run **offline** with a local model on llama.cpp.

## The labs

| # | Topic |
|---|---|
| 00 | Setup, Docker databases, payments glossary |
| 01 | Payment state machine with TDD |
| 02 | Minimal API + Dapper |
| 03 | Idempotency under parallel requests |
| 04 | SQL Server stored procedures and locking |
| 05 | Bare ADO.NET and bulk copy |
| 06 | Flow pipeline with `System.Threading.Channels` |
| 07 | BDD refund rules (Reqnroll) |
| 08 | A small C# MCP server |
| 09 | A tiny C# agent loop |
| 10 | .NET Framework → .NET Core (paper lab) |
| 11 | k3s on a Raspberry Pi *(optional)* |
| 12 | Semantic search with pgvector *(optional)* |
| 13 | Durable chargeback workflow |
| 14 | Observability and audit for agents |
| 15 | Context and caching |
| 16 | Failures, recovery, expected outcomes |
| 17 | Cold start at every layer |
| 18 | System design capstone |
| 19 | Human-in-the-loop approval UI *(optional)* |
| 20 | ETag and If-Match *(optional)* |

## Where things live

| Path | What | When you open it |
|---|---|---|
| [`docs/PROGRESS.md`](docs/PROGRESS.md) | the status board, one row per lab | first, every session |
| [`docs/EVERYDAY-COMMANDS.md`](docs/EVERYDAY-COMMANDS.md) | the command index: sends you to the right page | whenever you forget one |
| [`docs/commands/`](docs/commands) | the commands themselves, one topic per file | whenever you forget one |
| [`labs/NN-…/WORKBENCH.md`](labs) | the bench sheet you work through | while you work |
| [`labs/NN-…/TEST-LIST.md`](labs) | the test names and the rule each one checks — you write the bodies | from lab 02 on, while you work |
| [`docs/plan/PLAN.md`](docs/plan/PLAN.md) | the decisions (§7 open, §8 decided) | before you start a lab |
| [`docs/plan/CARRY-FORWARD.md`](docs/plan/CARRY-FORWARD.md) | the inbox: findings, ideas, fix-later | start and end of a lab |
| [`docs/plan/SPEC.md`](docs/plan/SPEC.md) | the inputs and limits this plan came from | rarely |
| [`docs/aperio/`](docs/aperio) | the Aperio backbone: map, tour, talking points | when writing an interview answer |
| [`db/README.md`](db/README.md) | the lab database plan | database labs |
| [`scripts/`](scripts) | shell helpers: `llm.sh` starts the local model named in `.env` | agent labs |
| `CLAUDE.md` | the rules the agent follows | when the agent gets it wrong |

The root holds two `.md` files, this one and `CLAUDE.md`. Everything else lives under [`docs/`](docs).
