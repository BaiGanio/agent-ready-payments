# Lab 00 — Setup + payments glossary

## What we solve
You can't build or talk about payments without the words, and you can't run the next labs without the databases.

## Simple example
A customer buys shoes online for €80. The shop **authorises** €80 (the bank holds the money). Two days later the shoes ship, so the shop **captures** €80 (the money moves). One shoe is wrong, so the shop **refunds** €40. The customer's bank is the **issuer**. The shop's bank is the **acquirer** — the role a payment company like ours often plays.

If you mix up "void" and "refund" in the interview, you sound like you've never seen a payment system. After this lab you won't.

## Why it matters for a payment platform
A payment company is often both an acquirer and a payment platform. Every story in their backlog uses these words.

## Everyday picture
A card payment is a **hotel key deposit**. At check-in the hotel *holds* €200 on your card (authorise). At check-out it *takes* what you actually spent (capture). If you cancel before arriving, it *lets go* of the hold (void). If the minibar charge was wrong, it *gives money back* (refund).

## Aperio comparison (our backbone)
| Case | Aperio | Where (verified 2026-10-17) | What this lab adds |
|---|---|---|---|
| Containers | ✅ Has | `docker/docker-compose.yml`, `docker-compose.prod.yml`, multi-stage `docker/Dockerfile` | SQL Server under Rosetta; two engines side by side |
| Branching | ⚠️ Partly | `master` + type-prefixed branches, Conventional Commits, Keep a Changelog, SemVer, `cd.release.yml` (`AGENTS.md` → Contribution Conventions) | Real **Gitflow**: `develop`, `release/*`, `--no-ff` merges |
| Money basics | ⚠️ Partly | `lib/handlers/database/amounts.js` `normalizeAmount()`: an ISO 4217 currency is required, and an unclear decimal separator is refused, not guessed | The payments domain and its words |
| Numbered migrations, two engines | ✅ Has | `db/migrations/` + `db/migrations-sqlite/`, 14 files each, kept in lockstep | DbUp in .NET, SQL Server |
| Clean data per test | ✅ Has | harness `mkdtemp` sandbox per scenario; `tests/mockDB.js`, `tests/mockStore.js` | Testcontainers + Respawn |

## Done when
- `docker compose up` gives you Postgres with pgvector (native) and SQL Server (Rosetta).
- `src/Payments.Db` creates the empty databases `payments_labs` and `PaymentsLabs` and runs DbUp on both. There are no tables yet. Each lab adds its own tables (see `db/README.md`).
- `develop` branch exists. Gitflow is ready.
- You can answer every question in `QA.md` without looking.

also---

# Spec (started 2026-10-18)

Step-by-step bench sheet: **`WORKBENCH.md`** in this folder. It explains each step and why it exists. Follow that while you work. This section is the contract: what must exist, and how you prove it.

## What you deliver

```
agent-ready-payments/
|-- .env                      (git-ignored: SQL Server sa password)
|-- .env.example              (committed: same keys, fake values)
|-- .gitignore
|-- docker/
|   `-- docker-compose.yml
|-- db/
|   |-- postgres/0000_baseline.sql
|   `-- sqlserver/0000_baseline.sql
|-- Payments.sln
`-- src/Payments.Db/          (DbUp runner: migrate, status)
```

Plus `labs/00-setup-and-glossary/GLOSSARY.md`, written in your own words.

## Hard requirements

| # | Requirement |
|---|---|
| 1 | Postgres is `pgvector/pgvector:pg17`, native arm64, on port 5432. SQL Server is `mcr.microsoft.com/mssql/server:2022-latest` with `platform: linux/amd64`, on port 1433. |
| 2 | Each container has a named volume and a healthcheck. |
| 3 | The `sa` password comes from `.env`. `.env` is git-ignored. `.env.example` is committed. |
| 4 | Compose does **not** create the lab databases. `src/Payments.Db` creates them with DbUp's `EnsureDatabase`. |
| 5 | `migrate --engine postgres|sqlserver` is safe to run again, and exits **1** on failure. |
| 6 | The journal table is DbUp's default `SchemaVersions` on both engines. |
| 7 | Lab 00 adds **no tables**. One baseline script per engine, kept in lockstep. |
| 8 | `develop` exists. Lab work happens on `feature/lab-00-setup`. |

## Done when

Every line must pass.

```bash
docker compose -f docker/docker-compose.yml ps          # both (healthy)
dotnet build                                            # 0 errors
dotnet run --project src/Payments.Db -- migrate --engine postgres
dotnet run --project src/Payments.Db -- migrate --engine sqlserver
dotnet run --project src/Payments.Db -- migrate --engine postgres   # 2nd run: no new scripts
dotnet run --project src/Payments.Db -- status --engine sqlserver   # 0 waiting
docker exec payments-postgres psql -U labs -d payments_labs -c "TABLE \"SchemaVersions\";"
git branch                                              # develop exists
```

- `GLOSSARY.md` exists and you wrote it.
- You can answer every question in `QA.md` without looking.

The second `migrate` printing "no new scripts" is the key line. It proves the journal works, which is what stops a migration running twice in production.

## No tests in this lab

There is nothing to test yet: no domain, no API. The `Done when` list is the test. Real tests start in Lab 01 (xUnit, pure domain) and Lab 02 (Testcontainers against Postgres).
