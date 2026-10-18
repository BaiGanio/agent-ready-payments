# CLAUDE.md

Rules for agents in this repo. Short and strict. We add, change and remove rules as the labs go.

## What this repo is

- Hands-on **labs**, not an app. Goal: prepare for a senior .NET payment backend role with AI agents.
- Read these first, in this order: `README.md` → `PLAN.md` (section 8, "Carry-forward notes") → the lab's `README.md` and `QA.md`.
- `SPEC.md` holds the inputs and limits. `PLAN.md` holds the decisions. If they disagree, `PLAN.md` wins (it is newer).

## Who writes the code

- **The user writes all lab code.** This is a learning repo. The code is the evidence.
- The agent writes: lab specs (`labs/NN-…/README.md`), `QA.md`, **failing tests**, docs, plans.
- The agent writes implementation code **only when the user asks for it**, for that one task. The permission ends with the task.
- When the user is stuck: give a hint first, then a small snippet, then the full answer. Go one step at a time.
- Never make a failing test pass by changing the test. Ask first.

## Tech rules

- Runnable code is **.NET 10**. .NET Framework is paper-only (Lab 10). It cannot run on macOS.
- Data access: **Dapper** first. **Bare ADO.NET** in Lab 05. **EF Core** only where `PLAN.md` ("Dapper vs EF Core") says so.
- Databases: **Postgres** for the API, Docker and k3s path. **SQL Server** (amd64 under Rosetta) for stored procedures and locking. **Never** target SQL Server at the Pi.
- Migrations: **DbUp** with plain `.sql` files. One migration per lab. See `db/README.md`.
- Tests: xUnit v3 (default until Lab 01 decides). Testcontainers for Postgres. Respawn between tests.
- BDD: **Reqnroll** (not SpecFlow).
- Agent labs run **offline**: `llama-server` + a local model. No API key needed.
- `Payments.Mcp` calls the API over HTTP. It **never** touches the database.
- Docker Desktop must be running before any container lab.

## Payment safety rules

- No real card data. Not in code, tests, seeds, logs, spans or prompts. Use test card numbers only.
- Every money-moving POST takes an `Idempotency-Key`.
- Destructive agent tools (refund, void) need a **two-phase confirm**.
- Agent keys never get the `payments:approve` scope.

## Writing style for docs

- Plain, simple English. Short sentences. Active voice. One idea per sentence.
- Explain a payment or agent word the first time it shows up.
- Every lab `README.md` opens with: **What we solve / Simple example / Why it matters for a payment platform**. Add an everyday picture (see `AGENT-READY-BACKEND.md`).
- Every lab `README.md` has an **"Aperio comparison"** table. Every `QA.md` ends with **Q-APERIO**.
- Use Mermaid for diagrams.
- Write absolute dates (`2026-10-18`), not "today" or "last week".

## Aperio

- Aperio lives at `/Users/lk/Projects/BaiGanio/aperio`. It is **read-only** for this repo. Never edit it. Other sessions may be active there.
- Check every Aperio claim against its code before you write it down.

## Git

Gitflow. The repo itself shows it.

- `main` — finished states only. Tag after each lab: `lab-00`, `lab-01`, …
- `develop` — integration branch. Created in Lab 00.
- `feature/lab-NN-short-name` — one per lab, from `develop`. Merge back with `--no-ff`.
- `release/x.y` — once, after Lab 08.
- `hotfix/…` — only for a real bug on `main`. Do not fake one.

Commits use **Conventional Commits**:

```
<type>(<scope>): <short summary in imperative mood>
```

- Types: `feat`, `fix`, `test`, `docs`, `refactor`, `chore`, `build`, `ci`.
- Scope: the lab (`lab-03`), or an area (`db`, `api`, `mcp`, `agent`, `plan`, `repo`).
- Examples: `test(lab-01): add failing tests for illegal transitions`, `docs(plan): decide xUnit v3`.
- TDD shows in history: a `test(...)` commit (red) comes before the `feat(...)` commit (green).
- The agent **never** runs `git commit`, `git push`, `git merge` or `git tag`. The user does all of them.
- When work is ready, the agent gives a suggested commit message and the files to stage. The user reviews and decides what to commit and when.

## Decisions

- When the user decides something, add it to `PLAN.md` section 7 or 8 with the date.
- Do not re-open a decision that is marked **Decided**.
