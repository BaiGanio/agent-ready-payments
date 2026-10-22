# Integration tests with containers (lab 02)

Testcontainers runs, and the containers they leave behind.

← back to [the command index](../EVERYDAY-COMMANDS.md)

---

| I want to | Command |
|---|---|
| Run only the API tests | `dotnet test tests/Payments.Api.Tests` |
| Run one group | `dotnet test tests/Payments.Api.Tests --filter "RefundTests"` |
| See the throwaway containers while tests run | `docker ps` |
| Kill a container a crashed test left behind | `docker rm -f <container id>` |
| Check what Docker socket the library will find | `docker context ls` |
| Run one class | `dotnet test tests/Payments.Api.Tests --filter-class "Payments.Api.Tests.ConcurrencyTests"` |
| Run one test | `dotnet test tests/Payments.Api.Tests --filter-method "*A_created_payment_can_be_read_back*"` |
| Run only the SQL Server tests (lab 04) | `dotnet test tests/Payments.Data.SqlServer.Tests` |
| Run a race test ten times | `for i in $(seq 1 10); do dotnet test Payments.slnx --configuration Release --filter-not-trait runs=local; done` |

The first run pulls `pgvector/pgvector:pg17` and takes about a minute. Later runs are seconds, because the image is cached. Testcontainers removes its own containers; if a fixture crashes before cleanup, its Ryuk reaper clears them within minutes, and `docker rm -f` is the impatient version.

## A race test is judged over many runs, not one

A test that fires 50 parallel requests can pass by luck. One green run means nothing, so
judge it over ten. The loop above is the whole technique.

Two things learned writing `ConcurrencyTests` (2026-10-22):

- **Run the whole solution, not the one class.** The `LoadAsync` snapshot bug showed up
  about 2 runs in 3 of the full suite and **never once** when `ConcurrencyTests` ran on
  its own. The machine has to be busy for the window to open.
- **Never put `--` in a `dotnet test` command.** It prints "Zero tests ran" and exits 5.
  See [`dotnet-build-and-test.md`](dotnet-build-and-test.md).
