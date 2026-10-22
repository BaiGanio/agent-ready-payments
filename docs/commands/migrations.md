# Migrations (DbUp)

Apply the `.sql` scripts and see what has run.

← back to [the command index](../EVERYDAY-COMMANDS.md)

---

One migration per lab per engine. Plain `.sql` files in `db/postgres/` and `db/sqlserver/`.

| I want to | Command |
|---|---|
| Apply new scripts to Postgres | `dotnet run --project src/Payments.Db -- migrate --engine postgres` |
| Apply new scripts to SQL Server | `dotnet run --project src/Payments.Db -- migrate --engine sqlserver` |
| See what has run and what waits (Postgres) | `dotnet run --project src/Payments.Db -- status --engine postgres` |
| Same for SQL Server | `dotnet run --project src/Payments.Db -- status --engine sqlserver` |

Run `migrate` **twice**. The second run must say there are no new scripts. That is the proof the journal works.

**Never edit a script from a finished lab.** DbUp will not run it again, so the two engines drift apart. Add a new numbered script instead.

The one exception: **the lab in flight may rewrite its own script** until the lab is tagged (`plan/PLAN.md` §8, 2026-10-22). DbUp skips a file name it has already run, so rebuild the dev database first:

| I want to | Command |
|---|---|
| Rebuild the SQL Server dev database from the scripts | `docker exec payments-mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -Q "ALTER DATABASE PaymentsLabs SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE PaymentsLabs;"` then `migrate --engine sqlserver` |
| Check RCSI is on (lab 04) | `docker exec payments-mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -Q "SELECT name, is_read_committed_snapshot_on FROM sys.databases"` |

Load the password first: `set -a; source .env; set +a`.
