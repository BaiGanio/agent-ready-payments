# Everyday Commands

Your cheat sheet. Copy from here. Do not memorise anything.

Run every command from the repo root: `/Users/lk/Projects/BaiGanio/agent-ready-payments`.

Replace `NN` with the lab number and `short-name` with the lab folder name. The folder names are in `labs/`.

---

## Read the docs

| I want to | Command |
|---|---|
| See the lab's steps | `open labs/NN-short-name/WORKBENCH.md` |
| See only the step titles | `grep -n '^## Step' labs/NN-short-name/WORKBENCH.md` |
| See the Traps table for this lab | `sed -n '/^## Traps/,/^## Notes/p' labs/NN-short-name/WORKBENCH.md` |
| Find a trap in **any** lab by error word | `grep -rn -i 'healthy' labs/*/WORKBENCH.md` |
| See one step only (step 4 here) | `sed -n '/^## Step 4/,/^## Step 5/p' labs/NN-short-name/WORKBENCH.md` |
| Read the QA **questions**, not the answers | `grep -E '^\*\*Q' labs/NN-short-name/QA.md` |
| See the open inbox | `open CARRY-FORWARD.md` |
| See what is already decided | `open PLAN.md` |

The `sed -n '/A/,/B/p' file` shape means: print from the line that matches `A` down to the line that matches `B`.

---

## Databases (Docker)

The compose file is **one** file with **two** services: `docker/docker-compose.yml`.

Always pass both flags. `-f` says which file. `--env-file` says where the SQL Server password comes from.

| I want to | Command |
|---|---|
| Start both databases | `docker compose -f docker/docker-compose.yml --env-file .env up -d` |
| Check they are healthy | `docker compose -f docker/docker-compose.yml --env-file .env ps` |
| Stop them, keep the data | `docker compose -f docker/docker-compose.yml --env-file .env stop` |
| Stop and remove containers, keep the data | `docker compose -f docker/docker-compose.yml --env-file .env down` |
| See why one will not start | `docker logs payments-mssql --tail 30` |
| Watch a log live (`Ctrl-C` to quit) | `docker logs payments-postgres -f` |
| Check Docker itself is up | `docker info` |

Both lines of `ps` must say `Up (healthy)`. `Up` alone is not ready yet. SQL Server needs 30–60 seconds on a cold start.

**Danger.** `down -v` also deletes the named volumes, so your databases are gone. Only use it when you want a clean slate.

---

## Talk to a database

| I want to | Command |
|---|---|
| Postgres shell | `docker exec -it payments-postgres psql -U labs -d payments_labs` |
| One Postgres query | `docker exec payments-postgres psql -U labs -d payments_labs -c "TABLE \"SchemaVersions\";"` |
| List Postgres tables | `docker exec payments-postgres psql -U labs -d payments_labs -c '\dt'` |
| SQL Server shell | `docker exec -it payments-mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d PaymentsLabs` |
| One SQL Server query | `docker exec payments-mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d PaymentsLabs -Q "SELECT * FROM SchemaVersions"` |

For the SQL Server lines to work, your shell must know the password. Load it once per terminal:

```bash
set -a; source .env; set +a
```

`set -a` means "export everything I load next". Without it, `source` keeps the value to itself and `docker exec` never sees it.

Inside `psql`: `\dt` lists tables, `\d Payment` describes one table, `\q` quits.

---

## Migrations (DbUp)

One migration per lab. Plain `.sql` files in `db/postgres/` and `db/sqlserver/`.

| I want to | Command |
|---|---|
| Apply new scripts to Postgres | `dotnet run --project src/Payments.Db -- migrate --engine postgres` |
| Apply new scripts to SQL Server | `dotnet run --project src/Payments.Db -- migrate --engine sqlserver` |
| See what has run and what waits (Postgres) | `dotnet run --project src/Payments.Db -- status --engine postgres` |
| Same for SQL Server | `dotnet run --project src/Payments.Db -- status --engine sqlserver` |

Run `migrate` **twice**. The second run must say there are no new scripts. That is the proof the journal works.

**Never edit a script that already ran.** DbUp will not run it again, so the two engines drift apart. Add a new numbered script instead.

---

## .NET

| I want to | Command |
|---|---|
| Check the SDK version | `dotnet --version` |
| Build everything | `dotnet build` |
| Run all tests | `dotnet test` |
| Run one test project | `dotnet test tests/Payments.Domain.Tests` |
| Run only matching tests | `dotnet test --filter "FullyQualifiedName~Refund"` |
| Show failures with detail | `dotnet test --logger "console;verbosity=detailed"` |
| Add a package to a project | `dotnet add src/Payments.Db package dbup-core` |
| Add a project to the solution | `dotnet sln add src/Payments.Api` |
| Clear a confusing build | `dotnet clean && dotnet build` |

---

## Debug with breakpoints (VS Code)

A **breakpoint** is a red dot on a line. The program stops there so you can look at the values.

| I want to | How |
|---|---|
| Open the project in VS Code | `code /Users/lk/Projects/BaiGanio/agent-ready-payments` |
| Set a breakpoint | Click left of the line number, or put the caret on the line and press `F9` |
| Start debugging | `F5`, then pick **Debug Payments.Db** the first time |
| Pass arguments (for example `migrate --engine postgres`) | Edit `"args"` in `.vscode/launch.json` |
| Step over one line | `F10` |
| Step into a method | `F11` |
| Continue to the next breakpoint | `F5` |
| Stop debugging | `Shift-F6` |
| See a value | Hover over it, or read the **Variables** panel |
| Test an expression while stopped | Type it in the **Debug Console** |
| Stop only when a condition is true | Right-click the breakpoint → **Edit Breakpoint** |
| Debug one test | Click **Debug Test** above the test method (needs the C# Dev Kit) |
| Attach to an already running process | `F5` → **Attach to a running .NET process** |

The debug run loads `.env`, so `MSSQL_SA_PASSWORD` is there. Start the databases first if the code needs them.

---

## Git (Gitflow)

You run every git command. The agent never commits, merges, pushes or tags.

| I want to | Command |
|---|---|
| Where am I? | `git status --short && git branch --show-current` |
| Start a lab | `git checkout develop && git checkout -b feature/lab-NN-short-name` |
| Stage one file | `git add path/to/file` |
| See what I staged, line by line | `git diff --staged` |
| Commit | `git commit -m "type(scope): summary"` |
| Fix the last commit message | `git commit --amend` |
| Unstage a file, keep the edits | `git restore --staged path/to/file` |
| Throw away edits in a file | `git restore path/to/file` |
| See the history as a picture | `git log --oneline --graph --all \| head -20` |
| List the tags | `git tag` |
| Finish a lab | see the "Close the lab" step in that lab's `WORKBENCH.md` |

Closing a lab, in full:

```bash
git checkout develop
git merge --no-ff feature/lab-NN-short-name
git checkout main
git merge --no-ff develop
git tag lab-NN
```

`--no-ff` forces a merge commit even when git could fast-forward. That commit is the marker that one lab finished.

Commit message shape: `<type>(<scope>): <summary in imperative mood>`. Types: `feat`, `fix`, `test`, `docs`, `refactor`, `chore`, `build`, `ci`. Scope is the lab (`lab-03`) or an area (`db`, `api`, `mcp`, `agent`, `plan`, `repo`).

The suggested message for each step is in that step's **Commit** part in `WORKBENCH.md`. You do not have to invent one.

---

## Safety check before any commit

```bash
git status --short          # .env must NOT appear
git diff --staged           # read every line you are about to commit
```

No real card data. Not in code, tests, seeds, logs or prompts. Test card numbers only.

---

## When something is broken

In this order:

1. `sed -n '/^## Traps/,/^## Notes/p' labs/NN-short-name/WORKBENCH.md` — the known pain for this lab.
2. `grep -rn -i '<your error word>' labs/*/WORKBENCH.md` — the known pain in every lab.
3. `docker compose -f docker/docker-compose.yml --env-file .env ps` — is the database even healthy?
4. `docker logs <container> --tail 50` — what the container says.
5. Write it in **Notes while you work** at the bottom of the lab's `WORKBENCH.md`, in the moment. It becomes a `CF-NNN` row in `CARRY-FORWARD.md` at the end of the lab.

---

## Housekeeping for this file

New command you had to look up twice? Add a row here in the same commit. Suggested message:

```
docs(repo): add <command> to everyday commands
```
