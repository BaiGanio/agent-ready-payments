# Migrations (DbUp)

Apply the `.sql` scripts and see what has run.

← back to [the command index](../EVERYDAY-COMMANDS.md)

---

One migration per lab. Plain `.sql` files in `db/postgres/` and `db/sqlserver/`.

| I want to | Command |
|---|---|
| Apply new scripts to Postgres | `dotnet run --project src/Payments.Db -- migrate --engine postgres` |
| Apply new scripts to SQL Server | `dotnet run --project src/Payments.Db -- migrate --engine sqlserver` |
| See what has run and what waits (Postgres) | `dotnet run --project src/Payments.Db -- status --engine postgres` |
| Same for SQL Server | `dotnet run --project src/Payments.Db -- status --engine sqlserver` |

Run `migrate` **twice**. The second run must say there are no new scripts. That is the proof the journal works.

**Never edit a script that already ran.** DbUp will not run it again, so the two engines drift apart. Add a new numbered script instead.
