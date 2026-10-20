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
| See where I am across all labs | `open docs/PROGRESS.md` |
| See the open inbox | `open docs/plan/CARRY-FORWARD.md` |
| See what is already decided | `open docs/plan/PLAN.md` |

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
| One Postgres query | `docker exec payments-postgres psql -U labs -d payments_labs -c 'TABLE schemaversions;'` |
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
| Run only matching tests | `dotnet test --filter "RefundTests"` |
| Re-run the tests on every save | `dotnet watch test --project tests/Payments.Domain.Tests` |
| List the test names without running them | `dotnet test -- --list-tests` |
| Add a package to a project | `dotnet add src/Payments.Db package dbup-core` |
| Add a project to the solution | `dotnet sln add src/Payments.Api` |
| Clear a confusing build | `dotnet clean && dotnet build` |

`dotnet test` needs the `test.runner` block in `global.json`. .NET 10 dropped the old VSTest host, and xUnit v3 test projects run on **Microsoft.Testing.Platform**. Without it you get "Testing with VSTest target is no longer supported". The filter is a plain substring of the test name, so `--filter "RefundTests"` runs one class and `--filter "Refund"` runs everything with that word in it.

`dotnet watch test` is the red-green loop: leave it running in one terminal, edit in the other, and the tests re-run on every save.

### The `TestResults/` folder

The test runner creates it by itself on `dotnet test`. Inside: a `.trx` file (one XML report of every test and its outcome) and, if you asked for coverage, a coverage file.

You do not need it. The terminal already prints `total / failed / succeeded`, and that is the whole proof a lab asks for. The folder only earns its place when a **CI server** must publish a report another tool reads, or when you want a coverage percentage. Neither happens in these labs yet.

It is git-ignored, nothing depends on it, and the next run rebuilds it. Delete it whenever it annoys you:

```bash
rm -rf TestResults
```

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

## The API (from lab 02)

| I want to | Command |
|---|---|
| Run the API | `dotnet run --project src/Payments.Api --urls http://localhost:5080` |
| Is it alive? | `curl -i http://localhost:5080/health` |
| Create a payment | `curl -i -X POST http://localhost:5080/payments -H 'Content-Type: application/json' -d '{"merchantId":"11111111-1111-1111-1111-111111111111","amount":100.00,"currency":"EUR"}'` |
| Read one payment | `curl -s http://localhost:5080/payments/<id> \| python3 -m json.tool` |
| Authorise / void it | `curl -i -X POST http://localhost:5080/payments/<id>/authorise` |
| Capture or refund part of it | `curl -i -X POST http://localhost:5080/payments/<id>/capture -H 'Content-Type: application/json' -d '{"amount":40.00,"currency":"EUR"}'` |
| List a merchant's payments | `curl -s 'http://localhost:5080/payments?merchant=11111111-1111-1111-1111-111111111111&limit=5' \| python3 -m json.tool` |
| See the headers only | add `-I`, or keep `-i` to get headers **and** body |
| Pretty-print any response | pipe it to `python3 -m json.tool` |

`-i` prints the status line and the headers with the body. That matters here: this lab's whole point is the status code, so a body alone is half the answer.

A failure body is `application/problem+json` and always carries a `code`. Read that field, not the English:

```bash
curl -s -X POST http://localhost:5080/payments/<id>/refund \
  -H 'Content-Type: application/json' -d '{"amount":9999.00,"currency":"EUR"}' \
  | python3 -c 'import sys,json; d=json.load(sys.stdin); print(d["status"], d["code"])'
```

---

## Integration tests with containers (from lab 02)

| I want to | Command |
|---|---|
| Run only the API tests | `dotnet test tests/Payments.Api.Tests` |
| Run one group | `dotnet test tests/Payments.Api.Tests --filter "RefundTests"` |
| See the throwaway containers while tests run | `docker ps` |
| Kill a container a crashed test left behind | `docker rm -f <container id>` |
| Check what Docker socket the library will find | `docker context ls` |

The first run pulls `pgvector/pgvector:pg17` and takes about a minute. Later runs are seconds, because the image is cached. Testcontainers removes its own containers; if a fixture crashes before cleanup, its Ryuk reaper clears them within minutes, and `docker rm -f` is the impatient version.

---

## Git (Gitflow)

You run every git command. The agent never commits, merges, pushes or tags.

| I want to | Command |
|---|---|
| Where am I? | `git status --short && git branch --show-current` |
| Is `develop` behind `main`? | `git log --oneline develop..main` (empty = level) |
| Bring `develop` level with `main` | `git checkout develop && git merge main` |
| Start a lab | `git checkout develop && git checkout -b feature/lab-NN-short-name` |
| Stage one file | `git add path/to/file` |
| See what I staged, line by line | `git diff --staged` |
| Commit | `git commit -m "type(scope): summary"` |
| Fix the last commit message | `git commit --amend` |
| Unstage a file, keep the edits | `git restore --staged path/to/file` |
| Throw away edits in a file | `git restore path/to/file` |
| See the history as a picture | `git log --oneline --graph --all \| head -20` |
| List the tags | `git tag` |
| List the branches | `git branch` |
| Delete a merged feature branch | `git branch -d feature/lab-NN-short-name` |
| Go to the end of a finished lab | `git checkout lab-NN` (then `git checkout develop` to come back) |
| Finish a lab | see the "Close the lab" step in that lab's `WORKBENCH.md` |
| Get out of vim when git opens it | `:wq` then **Enter** to save, or `:q!` then **Enter** to cancel |
| Stop git opening an editor at all | add `--no-edit` to the merge, or set a kinder editor once (below) |

Closing a lab, in full:

```bash
git checkout develop
git merge --no-ff --no-edit feature/lab-NN-short-name
git checkout main
git merge --no-ff --no-edit develop
git tag lab-NN
git checkout develop
git merge main                 # the back-merge
git branch -d feature/lab-NN-short-name
```

### The editor that opens on a merge

`git merge --no-ff` wants a commit message, so it opens your editor. Out of the box that is **vim**, which is a dark screen full of `~` and no way out that it tells you about. Two ways to never meet it:

- **`--no-edit`** on the merge, as above. Git keeps its own default message — `Merge branch 'feature/…' into develop` — which is exactly what you want for a lab merge. Nothing opens.
- **A kinder editor**, set once, for every git command that needs one:

```bash
git config --global core.editor "nano"
```

Nano lists its keys at the bottom of the screen: `Ctrl+O` then **Enter** saves, `Ctrl+X` exits.

If vim is already on your screen right now, the default message on line 1 is already correct:

| You want | Type | Then |
|---|---|---|
| Keep the message and finish the merge | `:wq` | **Enter** |
| Cancel — an empty message aborts the commit | `:q!` | **Enter** |

`:` opens vim's command line at the bottom, `w` writes, `q` quits, `!` means "even though I changed nothing".

**The back-merge is not optional.** Merging `develop` into `main` makes a merge commit that lives only on `main`, so `develop` is behind from that second on. The next feature branch would then start from a base missing `main`'s history, and a hotfix made on `main` would never reach `develop` at all. Check with `git log --oneline develop..main` — empty means level.

Never commit lab work straight to `main`. If it happened anyway and `main` is only ahead, fast-forward `develop` onto it:

```bash
git checkout develop
git merge --ff-only main
```

`--no-ff` forces a merge commit even when git could fast-forward. That commit is the marker that one lab finished.

`git branch -d` deletes the branch name only when its work is merged. A branch name is just a pointer at one commit, so this drops the pointer and keeps every commit. The tag `lab-NN` is the bookmark after that: `git checkout lab-NN` puts you at the exact end of that lab.

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
