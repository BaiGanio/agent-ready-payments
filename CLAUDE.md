# CLAUDE.md

Rules for agents in this repo. Short and strict. We add, change and remove rules as the labs go.

## Where things live

| Path | What |
|---|---|
| `README.md` | the front door |
| `CLAUDE.md` | these rules |
| `docs/PROGRESS.md` | the status board: where every lab stands |
| `docs/EVERYDAY-COMMANDS.md` | the command index: picks the right page in `docs/commands/` |
| `docs/commands/` | the command pages, one topic each: `git.md`, `ci.md`, `api.md`, `mcp.md`, `docker-and-databases.md`, … |
| `.github/workflows/ci.yml` | the PR gate: build and test on a PR into `main` |
| `docs/plan/PLAN.md` | decisions (§7 open, §8 decided) |
| `docs/plan/SPEC.md` | inputs and limits |
| `docs/plan/CARRY-FORWARD.md` | the inbox: findings, fix-later, questions, ideas |
| `docs/aperio/` | the Aperio backbone: map, tour, talking points, agent-ready notes |
| `labs/NN-…/` | three files per lab: `README.md`, `WORKBENCH.md`, `QA.md`. From lab 02 also `TEST-LIST.md`, plus any scaffold (`TDD-NOTES.md`, `STORE-NOTES.md`) |
| `db/README.md` | the lab database plan |

**The repo root holds two `.md` files only: `README.md` and `CLAUDE.md`.** Every other document lives under `docs/`. A new reference doc goes in `docs/plan/`, `docs/aperio/` or `docs/commands/`, or flat in `docs/` if it is none of those, and it gets a row in this table and in the one in `README.md`, in the same commit. Never add a `.md` file to the root.

## What this repo is

- Hands-on **labs**, not an app. Goal: prepare for a senior .NET payment backend role with AI agents.
- Read these first, in this order: `README.md` → `docs/plan/PLAN.md` section 8 (**Decided**) → `docs/plan/CARRY-FORWARD.md` (the open inbox) → the lab's `README.md`, `WORKBENCH.md` and `QA.md`.
- `docs/plan/SPEC.md` holds the inputs and limits. `docs/plan/PLAN.md` holds the decisions. If they disagree, `docs/plan/PLAN.md` wins (it is newer).
- `docs/EVERYDAY-COMMANDS.md` is the command **index**; the commands themselves live one topic per file in `docs/commands/`. When a lab introduces a command the user will run again, add a row to the page that owns it, in the same commit. A new topic gets a new page plus a row in the index. Keep every page small enough to read at once — if one passes ~120 lines, split it.

## Carry-forward

`docs/plan/CARRY-FORWARD.md` is the **inbox**: findings, fix-later, open questions, ideas for later. `docs/plan/PLAN.md` section 8 is **decided** things only. An item never lives in both.

- Noticed something outside the current lab's scope? Add a **one-line row** to `docs/plan/CARRY-FORWARD.md` with the next free `CF-NNN` id and the date. Do not widen the current lab to deal with it.
- If it needs a paragraph, it is a decision. Put it in `docs/plan/PLAN.md` section 8 instead.
- At the end of each lab: move the lab's **"Notes while you work"** into `docs/plan/CARRY-FORWARD.md`, then groom the inbox. An item graduates to `docs/plan/PLAN.md` §8, to a lab `README.md`, or it is dropped. Set its status, move the row to **Closed**, and never reuse the id.
- The agent may add and groom rows. Only the user decides a graduation.

## Who writes the code

- **The user decides, per task, who writes the code.** This is a learning repo, so writing it yourself is the learning. But the choice is the user's each time, not a standing rule.
- **The verb picks the place.** The user says it, the agent obeys it, and never swaps one for the other:

| The user says | The agent does |
|---|---|
| "**give** me the tests / the implementation" | prints the code in the chat, in a fenced block. Touches no file. |
| "**write** me the tests / the implementation" | writes the code to disk at the right path, builds it, runs it, then reports. |

- No detours. On "write", do not offer a scratchpad copy plus `cp` commands, and do not re-raise the learning rule — the user knows it and chose.
- The agent writes by default, without being asked: lab specs (`labs/NN-…/README.md`), `WORKBENCH.md`, `QA.md`, docs, plans, scaffolds, and the **test list** — `labs/NN-…/TEST-LIST.md`: one line per test name, grouped, with the rule each test must check. Test bodies and implementation code need the user's word first.
- **Lab 01 note:** the agent wrote the test bodies in `tests/Payments.Domain.Tests/`. They stay as the worked example of what good test code looks like.
- One ask, one task. Permission to write code ends with that task; the next task needs the verb again.
- When the user is stuck: give a hint first, then a small snippet, then the full answer. Go one step at a time.
- Never make a failing test pass by changing the test. Ask first.

## Tech rules

- Runnable code is **.NET 10**. .NET Framework is paper-only (Lab 10). It cannot run on macOS.
- Data access: **Dapper** first. **Bare ADO.NET** in Lab 05. **EF Core** only where `docs/plan/PLAN.md` ("Dapper vs EF Core") says so.
- Databases: **Postgres** for the API, Docker and k3s path. **SQL Server** (amd64 under Rosetta) for stored procedures and locking. **Never** target SQL Server at the Pi.
- Migrations: **DbUp** with plain `.sql` files. One migration per lab per engine. See `db/README.md`.
- **Lockstep, in the same commit.** Every table the API reads or writes exists in **both** engines. A new Postgres migration gets its SQL Server twin in the same commit, and the other way round. Lockstep is about columns, types and rules — not spelling (`snake_case` in Postgres, `PascalCase` in SQL Server). An engine-only column is allowed and the script must say so in a comment; `db/README.md` lists the ones we have.
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
- Every lab `README.md` opens with: **What we solve / Simple example / Why it matters for a payment platform**. Add an everyday picture (see `docs/aperio/AGENT-READY-BACKEND.md`).
- Every lab `README.md` has an **"Aperio comparison"** table. Every `QA.md` ends with **Q-APERIO**.
- **Three files per lab.** `README.md` = the spec (what we solve, Aperio comparison, what you deliver, hard requirements, "Done when"). `WORKBENCH.md` = the ordered bench sheet the user follows with their hands. `QA.md` = interview questions. Steps live in `WORKBENCH.md` only, never in both.
- Every `WORKBENCH.md` step has five parts: **Goal** (one line), **Why** (3–4 sentences: what we accomplish and why it is done this way), **Do** (exact commands and files), **Proof** (how you know it worked), **Commit** (the suggested commit message for this step). The `Why` is the point of the file — never drop it. End the file with a **Traps** table and a **Notes while you work** section.
- **A commit per step.** Every step that changes a file ends with a **Commit** part: the files to stage and one Conventional Commit message in a fenced block, ready to copy. A step that changes no file (read the questions, start Docker, say the answers out loud) says `**Commit.** Nothing to commit.` One step is one commit, so the history reads as the bench sheet. The user runs `git commit`; the agent only writes the message.
- **`QA.md` is read twice, differently.** Every `WORKBENCH.md` starts with **Step 0 — read the questions, not the answers** (5 min, `grep -E '^\*\*Q' …/QA.md`), and ends — just before "Close the lab" — with **say the answers out loud**, then check and mark the misses. Reading the answers upfront spoils the test: it turns recall into recognition.
- **Scaffolds.** When a lab's deliverable is a document the user must write **in their own words** (a glossary, a migration note, a design doc, an evaluation set), the agent creates the file at the start of the lab with the structure only: headings, an empty bullet or placeholder per item, the rule the content must pass, and a self-check list. The agent **never fills the content** — that is the learning. Mark it in the file: `Written by: **you**`. Examples: Lab 00 `GLOSSARY.md`, Lab 10 the migration note, Lab 18 the design doc.
- **When to write a `WORKBENCH.md`:** at the start of that lab, not in advance. Copy `labs/_TEMPLATE/WORKBENCH.md` and fill it. A workbench written three labs early is wrong by the time you reach it, because earlier labs change the decisions.
- `labs/_TEMPLATE/WORKBENCH.md` is the blueprint. When a lab teaches a better shape, change the blueprint too, in the same commit.

## Diagrams

Visuals come first in this repo. The user learns from them.

- **Mermaid only.** No images, no ASCII art. Mermaid renders in GitHub and in the editor, and it lives in git as text you can diff.
- Every lab `README.md` has at least one diagram: what the lab builds.
- Every lab `WORKBENCH.md` opens with a **"The target state"** diagram, before step 1. If it cannot be drawn, the lab is not specified yet.
- Add a diagram inside any step where the shape is hard to hold in your head. Pick the type that fits:

| Shape | Mermaid type |
|---|---|
| Boxes and who calls whom | `flowchart LR` |
| Who talks to whom, in what order | `sequenceDiagram` |
| Allowed and illegal moves (payment status, workflow) | `stateDiagram-v2` |
| Tables and keys | `erDiagram` |
| Branches, merges, tags | `gitGraph` |
| Locks, retries, timeouts over time | `sequenceDiagram` with notes, or `gantt` |

- Label every arrow. An unlabelled arrow teaches nothing.
- Keep one idea per diagram. Two small diagrams beat one crowded one.
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
- **Always back-merge `main` into `develop`.** Anything that lands on `main` — the lab merge commit, a hotfix, a release — is not on `develop` until you merge it back. Skip it and `develop` falls behind, every new feature branch starts from a stale base, and the next merge fights over commits that are already on `main`. The "Close the lab" step does it in one line: `git checkout develop && git merge main`.
- **Never commit lab work straight to `main`.** Lab prep (spec, bench sheet, tests, scaffolds) is lab work: it belongs on `feature/lab-NN-…`, same as the code.

Keep [`docs/PROGRESS.md`](docs/PROGRESS.md) true. It is the status board: one row per lab plus a tick list for the lab in flight. The row changes in the same commit as the work it describes, and a lab becomes `Done` only in its "Close the lab" step.

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

- When the user decides something, add it to `docs/plan/PLAN.md` section 7 or 8 with the date.
- Do not re-open a decision that is marked **Decided**.
