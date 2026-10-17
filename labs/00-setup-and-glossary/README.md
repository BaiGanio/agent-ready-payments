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

Spec + steps: written when the lab starts.
