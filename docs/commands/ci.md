# CI and the PR gate

Run what the GitHub runner runs, before you push.

← back to [the command index](../EVERYDAY-COMMANDS.md)

---

## The commands

| I want to | Command |
|---|---|
| Run exactly what CI runs | `dotnet test Payments.slnx --configuration Release --filter-not-trait runs=local` |
| Run only the tests CI **cannot** run | `dotnet test Payments.slnx --filter-trait runs=local` |
| Build the way CI builds | `dotnet build Payments.slnx --configuration Release` |
| Check the workflow file is valid | `actionlint` |
| See the last runs | `gh run list --limit 5` |
| Watch the run for this branch | `gh run watch` |
| Read why a run failed | `gh run view --log-failed` |
| Open the PR that is gated | `gh pr create --base main --head develop` |

Docker Desktop must be running, because the API tests start Postgres with Testcontainers. Measured 2026-10-22: 324 tests, 0 failed, 9.5 s.

---

## How a test says CI cannot run it

A test that needs something a GitHub runner does not have gets one attribute:

```csharp
[Fact]
[Trait("runs", "local")]
public void Needs_sql_server_under_rosetta() { }
```

CI then skips it, because the workflow runs `--filter-not-trait runs=local`.

**The tag marks the exceptions, not the rule.** A new test runs in CI unless you tag it. Forget a tag and CI goes red loudly, which is the safe way to be wrong. The other way round — tagging every test CI *may* run — means a forgotten tag silently never runs.

Nothing carries the tag today. It belongs on lab 04's SQL Server tests (the amd64 image under Rosetta is a Mac trick), lab 09's `llama-server` tests, and anything needing the Pi (lab 11).

---

## Traps

| Trap | What happens | Fix |
|---|---|---|
| `--` in a `dotnet test` command | **"Zero tests ran", exit 5.** The passthrough is broken for this runner, even for `-- -noLogo` | Put every option straight on the command, no separator |
| A misspelt filter option | Also "Zero tests ran", exit 5 | Non-zero, so CI goes red, never silently green. But read it as "wrong option name", not "the filter matched nothing" |
| You need an xUnit single-dash flag | `dotnet test` will not pass it through | Run the project as a program: `dotnet run --project tests/Payments.Domain.Tests -- -trait- "runs=local"` |
| The gate is green but `main` still accepts a bad merge | Branch protection was never turned on | GitHub → Settings → Branches → protect `main` → require the `build-and-test` check |

---

## The workflow

It is `.github/workflows/ci.yml`, 28 lines. It runs on a pull request into `main` (the gate) and on a push to `develop` (early warning). The SDK comes from `global.json`, so the runner matches this Mac.
