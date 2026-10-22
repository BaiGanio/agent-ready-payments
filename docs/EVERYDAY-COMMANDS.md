# Everyday Commands

Your cheat sheet, split by topic. Copy from here. Do not memorise anything.

Run every command from the repo root: `/Users/lk/Projects/BaiGanio/agent-ready-payments`.

Replace `NN` with the lab number and `short-name` with the lab folder name. The folder names are in `labs/`.

---

## Pick a page

| I am doing | Page |
|---|---|
| Reading a lab, hunting a trap, working out what broke | [`commands/finding-things.md`](commands/finding-things.md) |
| Starting the databases, opening a `psql` shell | [`commands/docker-and-databases.md`](commands/docker-and-databases.md) |
| Applying `.sql` scripts | [`commands/migrations.md`](commands/migrations.md) |
| Building, running tests, filtering tests | [`commands/dotnet-build-and-test.md`](commands/dotnet-build-and-test.md) |
| Stopping on a line to look at values | [`commands/debugging.md`](commands/debugging.md) |
| `curl`-ing the payment endpoints | [`commands/api.md`](commands/api.md) |
| Making, storing or revoking an API key | [`commands/api-keys.md`](commands/api-keys.md) |
| Driving the MCP tools by hand | [`commands/mcp.md`](commands/mcp.md) |
| Running the Testcontainers tests | [`commands/integration-tests.md`](commands/integration-tests.md) |
| Branching, committing, merging, tagging | [`commands/git.md`](commands/git.md) |
| Running what CI runs, or fixing a red gate | [`commands/ci.md`](commands/ci.md) |

---

## Search all of them at once

The pages are small on purpose, but you should not have to guess which one. Grep the lot:

```bash
grep -rn "docker compose" docs/commands/
```

To see every "I want to" line in one list:

```bash
grep -rh '^| ' docs/commands/ | grep -v '^| *---'
```

To find which page owns a topic:

```bash
grep -rl "Idempotency-Key" docs/commands/
```

---

## The five you use every day

| I want to | Command |
|---|---|
| Start both databases | `docker compose -f docker/docker-compose.yml --env-file .env up -d` |
| Run all tests | `dotnet test` |
| Run what CI runs | `dotnet test Payments.slnx --configuration Release --filter-not-trait runs=local` |
| Where am I in git? | `git status --short && git branch --show-current` |
| Is `develop` behind `main`? | `git log --oneline develop..main` (empty = level) |

---

## Housekeeping for these files

New command you had to look up twice? Add a row to the page that owns it, in the same commit. A new topic gets a new page in `docs/commands/` plus a row in the table above. Suggested message:

```
docs(repo): add <command> to everyday commands
```

**Never put `--` in a `dotnet test` command.** It prints "Zero tests ran" and exits 5. See [`commands/ci.md`](commands/ci.md) → Traps.
