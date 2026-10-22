# Build and test (.NET)

`dotnet build`, `dotnet test`, the filters, and the `--` trap.

← back to [the command index](../EVERYDAY-COMMANDS.md)

---

| I want to | Command |
|---|---|
| Check the SDK version | `dotnet --version` |
| Build everything | `dotnet build` |
| Run all tests | `dotnet test` |
| Run one test project | `dotnet test tests/Payments.Domain.Tests` |
| Run only matching tests | `dotnet test --filter "RefundTests"` |
| Re-run the tests on every save | `dotnet watch test --project tests/Payments.Domain.Tests` |
| List the test names without running them | `dotnet test tests/Payments.Domain.Tests --list-tests` |
| Run what CI runs | `dotnet test Payments.slnx --filter-not-trait runs=local` |
| Run only the tests CI cannot run | `dotnet test Payments.slnx --filter-trait runs=local` |
| Add a package to a project | `dotnet add src/Payments.Db package dbup-core` |
| Add a project to the solution | `dotnet sln add src/Payments.Api` |
| Clear a confusing build | `dotnet clean && dotnet build` |

`dotnet test` needs the `test.runner` block in `global.json`. .NET 10 dropped the old VSTest host, and xUnit v3 test projects run on **Microsoft.Testing.Platform**. Without it you get "Testing with VSTest target is no longer supported". The filter is a plain substring of the test name, so `--filter "RefundTests"` runs one class and `--filter "Refund"` runs everything with that word in it.

`dotnet watch test` is the red-green loop: leave it running in one terminal, edit in the other, and the tests re-run on every save.

**Never put `--` in a `dotnet test` command.** Measured 2026-10-22: `dotnet test <project> -- <anything>` prints **"Zero tests ran"** and exits 5, even for a harmless flag like `-- -noLogo`. The passthrough is broken for this runner, so every option goes straight on the command with no separator. The old `dotnet test -- --list-tests` row in this file was wrong for that reason.

If you do need xUnit's own single-dash flags, run the test project as a program instead:

```bash
dotnet run --project tests/Payments.Domain.Tests -- -trait- "runs=local"
```

A misspelt filter option also gives "Zero tests ran" and exit 5. That is non-zero, so CI goes red. A typo in a filter can never pass silently — but it also means "Zero tests ran" is your clue that the option name is wrong, not that the filter matched nothing.

## The `TestResults/` folder

The test runner creates it by itself on `dotnet test`. Inside: a `.trx` file (one XML report of every test and its outcome) and, if you asked for coverage, a coverage file.

You do not need it. The terminal already prints `total / failed / succeeded`, and that is the whole proof a lab asks for. The folder only earns its place when a **CI server** must publish a report another tool reads, or when you want a coverage percentage. Neither happens in these labs yet.

It is git-ignored, nothing depends on it, and the next run rebuilds it. Delete it whenever it annoys you:

```bash
rm -rf TestResults
```
