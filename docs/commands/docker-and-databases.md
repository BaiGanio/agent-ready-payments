# Docker and the databases

Start the two databases, check they are healthy, open a shell.

← back to [the command index](../EVERYDAY-COMMANDS.md)

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

Ports on your Mac: Postgres is **55432**, SQL Server is **51433**. Inside the containers they stay 5432 and 1433. The odd host ports keep clear of any other database you already run. Docker Desktop shows the stack as **agent-ready-payments**.

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

## Locks and deadlocks (lab 04)

| I want to | Command |
|---|---|
| See who is blocked, and by whom | `docker exec payments-mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -Q "SELECT session_id, blocking_session_id, wait_type, wait_time FROM sys.dm_exec_requests WHERE blocking_session_id <> 0"` |
| Save the last deadlock graph to a file | the `system_health` query in `labs/04-sqlserver-procs-locking/WORKBENCH.md` step 15 |
| List test databases a crashed run left behind | `docker exec payments-mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -Q "SELECT name FROM sys.databases WHERE name LIKE 'PaymentsLabs_Test_%'"` |

Any query that uses XML methods (`.value`, `.query`) needs `sqlcmd -I`. Without it: `Msg 1934 … QUOTED_IDENTIFIER`.
