# Lab NN — Workbench

> **Blueprint.** Copy this file to `labs/NN-short-name/WORKBENCH.md` when the lab starts.
> Replace every `NN`, every `<…>` and every `TODO`. Delete this blockquote.
> Rules for this file live in `CLAUDE.md` → "Writing style for docs".

Follow the steps in order. Each step says **Goal**, **Why**, **Do**, **Proof**.

Spec and Done-when list: `README.md` in this folder. Interview questions: `QA.md`.

Started YYYY-MM-DD.

## The target state

One diagram of what exists when the lab is done. Draw this **before** step 1 — if you cannot draw it, the lab is not specified yet.

```mermaid
flowchart LR
    TODO["TODO: boxes you will build"] --> TODO2["TODO: what they talk to"]
```

---

## Step 0 — Read the questions, not the answers

**Goal.** You have skimmed every question in `QA.md`. 5 minutes. No answers read.

**Why.** Knowing the questions first changes what you notice while you work, and attention is most of the learning. Reading the **answers** now would spoil the test — afterwards you would *recognise* them instead of *recalling* them, which feels like knowing and does not survive an interview. So eyes off every `A:` line. If one question makes no sense at all, read that single answer and move on.

**Do.**

```bash
grep -E '^\*\*Q' labs/NN-short-name/QA.md
```

**Proof.** You can say, in one sentence, what this lab is going to be asked about.

---

## Step 1 — <short title>

**Goal.** One line. What exists when this step is done.

**Why.** 3–4 sentences. What we accomplish, and why it is done this way and not another way. Name the thing that goes wrong if you skip it. This is the part that teaches; never shorten it to one line.

**Do.**

```bash
# exact commands
```

**Proof.** The command or output that shows it worked.

---

## Step 2 — <short title>

**Goal.**

**Why.**

**Do.**

**Proof.**

<!-- Add a diagram inside any step where the shape is hard to hold in your head:
     a flow, a sequence, a state machine, a branch history, a lock timeline. -->

---

## Step N-1 — Say the answers out loud

**Goal.** You answered every question in `QA.md` out loud, then checked.

**Why.** This is the test the lab was for. Speaking the answer uses a different path than reading it, and it is the path an interview uses. Mark every question you got wrong or said badly — those marked ones are a 5-minute warm-up at the start of your next session, and that repeat is where most of the gain sits.

**Do.**

1. Cover the file. Work down the question list.
2. Say the answer out loud. Then read the real answer.
3. Mark the misses. Read the **Trap** follow-up for each one.
4. Answer `Q-APERIO` last: "Have you solved this before?"

**Proof.** You answered every question without looking. The marked list is short.

---

## Step N — Close the lab

**Goal.** The lab is merged to `develop`, and `main` carries the `lab-NN` tag.

**Why.** The merge and the tag are the record that a lab finished. `--no-ff` keeps a merge commit even when a fast-forward is possible, so the history shows the lab as one unit of work. Every lab repeats this, so the shape stays the same.

**Do.** Run the full Done-when list in `README.md` first. Suggested commits, smallest first:

```
test(lab-NN): <the red commit, if the lab has tests>
feat(lab-NN): <the green commit>
docs(lab-NN): <notes>
```

```bash
git checkout develop
git merge --no-ff feature/lab-NN-short-name
git checkout main
git merge --no-ff develop
git tag lab-NN
```

You run every git command. The agent never commits, merges, pushes or tags.

**Proof.**

```bash
git log --oneline --graph --all | head -20
git tag
```

---

## Traps

| Trap | What happens | Fix |
|---|---|---|
| TODO | TODO | TODO |

## Notes while you work

Write what surprised you here, as it happens. At the end of the lab these move into `CARRY-FORWARD.md` (the inbox) as `CF-NNN` rows. From there each one graduates to `PLAN.md` section 8, to a lab `README.md`, or it is dropped.

-
