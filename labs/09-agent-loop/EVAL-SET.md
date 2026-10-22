# Lab 09 — Evaluation set

Written by: **you**

The agent wrote the structure only: the headings, the empty fields, the rules and the self-check. Every scenario, every expectation and every score is yours. That is the learning: deciding what "the agent did it right" means is the hard part of building an agent.

## What it is for

It decides **which local model** this repo uses (`docs/plan/PLAN.md` §7, "Local model"). The smallest rung of the ladder that passes wins. Not the model you like, not the newest one: the one the numbers pick.

## The rules every scenario must pass

1. **One request, one expected end.** If two endings are both fine, write both down.
2. **The expected tools are names, in order.** Arguments only where they matter (the payment id, the amount).
3. **The final state is checkable without reading the answer's style.** A run status, a row count, a code in the last tool step, a word that must or must not appear.
4. **At least one forbidden thing per scenario.** A confirm by the model, a refund on the wrong payment, the word "refunded" when nothing was refunded, a retry loop on `not_found`.
5. **No real card data and no real names.** Test merchants M1 and M2 only.
6. **Cover the guards.** Over the ten: at least one bad amount, one missing payment, one amount above what is left, one question with no tool, one refund a human denies, one request a weak model tends to loop on.
7. **The README's five ideas are a start, not the set.** Rewrite them in your words; add five of your own.

## The scenarios

### S01 —

- **Request** (the exact words typed):
- **Arrange** (payments to create first, with status and amounts):
- **Approval** (`yes`, `no`, or `never asked`):
- **Expected tools, in order**:
- **Expected final state** (run status, refund rows, what the answer must say):
- **Forbidden** (calls or words that fail this scenario at once):
- **Max steps**:
- **Why this scenario** (one line: which mistake does it catch?):

### S02 —

- **Request** (the exact words typed):
- **Arrange** (payments to create first, with status and amounts):
- **Approval** (`yes`, `no`, or `never asked`):
- **Expected tools, in order**:
- **Expected final state** (run status, refund rows, what the answer must say):
- **Forbidden** (calls or words that fail this scenario at once):
- **Max steps**:
- **Why this scenario** (one line: which mistake does it catch?):

### S03 —

- **Request** (the exact words typed):
- **Arrange** (payments to create first, with status and amounts):
- **Approval** (`yes`, `no`, or `never asked`):
- **Expected tools, in order**:
- **Expected final state** (run status, refund rows, what the answer must say):
- **Forbidden** (calls or words that fail this scenario at once):
- **Max steps**:
- **Why this scenario** (one line: which mistake does it catch?):

### S04 —

- **Request** (the exact words typed):
- **Arrange** (payments to create first, with status and amounts):
- **Approval** (`yes`, `no`, or `never asked`):
- **Expected tools, in order**:
- **Expected final state** (run status, refund rows, what the answer must say):
- **Forbidden** (calls or words that fail this scenario at once):
- **Max steps**:
- **Why this scenario** (one line: which mistake does it catch?):

### S05 —

- **Request** (the exact words typed):
- **Arrange** (payments to create first, with status and amounts):
- **Approval** (`yes`, `no`, or `never asked`):
- **Expected tools, in order**:
- **Expected final state** (run status, refund rows, what the answer must say):
- **Forbidden** (calls or words that fail this scenario at once):
- **Max steps**:
- **Why this scenario** (one line: which mistake does it catch?):

### S06 —

- **Request** (the exact words typed):
- **Arrange** (payments to create first, with status and amounts):
- **Approval** (`yes`, `no`, or `never asked`):
- **Expected tools, in order**:
- **Expected final state** (run status, refund rows, what the answer must say):
- **Forbidden** (calls or words that fail this scenario at once):
- **Max steps**:
- **Why this scenario** (one line: which mistake does it catch?):

### S07 —

- **Request** (the exact words typed):
- **Arrange** (payments to create first, with status and amounts):
- **Approval** (`yes`, `no`, or `never asked`):
- **Expected tools, in order**:
- **Expected final state** (run status, refund rows, what the answer must say):
- **Forbidden** (calls or words that fail this scenario at once):
- **Max steps**:
- **Why this scenario** (one line: which mistake does it catch?):

### S08 —

- **Request** (the exact words typed):
- **Arrange** (payments to create first, with status and amounts):
- **Approval** (`yes`, `no`, or `never asked`):
- **Expected tools, in order**:
- **Expected final state** (run status, refund rows, what the answer must say):
- **Forbidden** (calls or words that fail this scenario at once):
- **Max steps**:
- **Why this scenario** (one line: which mistake does it catch?):

### S09 —

- **Request** (the exact words typed):
- **Arrange** (payments to create first, with status and amounts):
- **Approval** (`yes`, `no`, or `never asked`):
- **Expected tools, in order**:
- **Expected final state** (run status, refund rows, what the answer must say):
- **Forbidden** (calls or words that fail this scenario at once):
- **Max steps**:
- **Why this scenario** (one line: which mistake does it catch?):

### S10 —

- **Request** (the exact words typed):
- **Arrange** (payments to create first, with status and amounts):
- **Approval** (`yes`, `no`, or `never asked`):
- **Expected tools, in order**:
- **Expected final state** (run status, refund rows, what the answer must say):
- **Forbidden** (calls or words that fail this scenario at once):
- **Max steps**:
- **Why this scenario** (one line: which mistake does it catch?):

## Scores

One row per model per scenario is too much to read. One row per model, the totals:

| Rung | Model | Scenarios passed (of 10) | Forbidden calls | Mean steps | Mean seconds | Notes |
|---|---|---|---|---|---|---|
| 1 | `unsloth/gemma-4-E4B-it-qat-GGUF:UD-Q4_K_XL` | | | | | |
| 3 | `unsloth/gemma-4-26B-A4B-it-GGUF` | | | | | |

Which scenarios each model failed, and how:

-

## The pick

- **Chosen model**:
- **Why** (the numbers, in one or two sentences):
- **What the smaller one got wrong** (the guard stories for the interview):

The pick becomes a dated line in `docs/plan/PLAN.md` §8, and the §7 row "Local model" is struck through. Only you decide it.

## Self-check before you run it

- [ ] Ten scenarios, each with every field filled.
- [ ] Every scenario has at least one forbidden thing.
- [ ] Rule 6: every guard is covered at least once.
- [ ] A friend could run each scenario from this file alone, without asking you anything.
- [ ] No expectation depends on the model's wording, only on facts.
