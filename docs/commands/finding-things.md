# Finding things

Read a lab, find a trap, work out what broke.

← back to [the command index](../EVERYDAY-COMMANDS.md)

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

## When something is broken

In this order:

1. `sed -n '/^## Traps/,/^## Notes/p' labs/NN-short-name/WORKBENCH.md` — the known pain for this lab.
2. `grep -rn -i '<your error word>' labs/*/WORKBENCH.md` — the known pain in every lab.
3. `docker compose -f docker/docker-compose.yml --env-file .env ps` — is the database even healthy?
4. `docker logs <container> --tail 50` — what the container says.
5. Write it in **Notes while you work** at the bottom of the lab's `WORKBENCH.md`, in the moment. It becomes a `CF-NNN` row in `CARRY-FORWARD.md` at the end of the lab.
