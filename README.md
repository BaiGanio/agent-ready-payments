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

Where you are right now: [`PROGRESS.md`](PROGRESS.md) — the status board, one row per lab.

Full details: [`PLAN.md`](PLAN.md). Inputs and constraints: [`SPEC.md`](SPEC.md). Open findings, ideas and fix-later items: [`CARRY-FORWARD.md`](CARRY-FORWARD.md). Every command you need, in one place: [`EVERYDAY-COMMANDS.md`](EVERYDAY-COMMANDS.md).
