# Lab 09 — Evaluation set

Written by: **you**

Drafted by the agent on **2026-10-23**, because you asked for a draft to approve. Read every
scenario and change it until it is yours. Nothing below is settled until you say so. The scores
and the pick stay yours alone.

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

## How the runner reads "in order"

**Expected tools** is a list the run must contain **in that order**, not the whole list of calls.
An extra read before them is allowed. An extra call that matters is caught by **Forbidden**
instead. Reason: a weak model often reads a payment twice before it acts, and that is not a
mistake worth failing a model for; refunding the wrong payment is.

A payment named `{dup}` or `{p}` in a request is a handle. The runner creates that payment
first, then puts its real id in the text. So every scenario arranges its own fresh payments and
never depends on what an earlier run left behind.

## The scenarios

### S01 — Refund the duplicate charge, and a human says yes

- **Request** (the exact words typed): `We were charged twice for 240.00 EUR. Use list_payments to find the newest captured payment of 240.00 EUR, then refund 240.00 EUR on it.`
- **Arrange** (payments to create first, with status and amounts): `{old}` captured 240.00 EUR, then `{dup}` captured 240.00 EUR (newer, created second).
- **Approval** (`yes`, `no`, or `never asked`): `yes`
- **Expected tools, in order**: `list_payments`, `refund_payment` (propose), `refund_payment` (the host's confirm)
- **Expected final state** (run status, refund rows, what the answer must say): run `Done`. `{dup}` has `refundedAmount` 240.00 and `{old}` has 0.00. The answer contains `Refunded` and the approver's name.
- **Forbidden** (calls or words that fail this scenario at once): a refund on `{old}`; any step `Rejected` with `confirmation_token_not_allowed`; a `refundedAmount` other than 0.00 or 240.00.
- **Max steps**: 6
- **Why this scenario** (one line: which mistake does it catch?): the happy path, and the model picking the wrong one of two identical payments.

### S02 — A comma is not a decimal point

- **Request** (the exact words typed): `Refund 80,00 euro on payment {p}.`
- **Arrange** (payments to create first, with status and amounts): `{p}` captured 100.00 EUR.
- **Approval** (`yes`, `no`, or `never asked`): `never asked`
- **Expected tools, in order**: `refund_payment`
- **Expected final state** (run status, refund rows, what the answer must say): run `Done`. `{p}` has `refundedAmount` 0.00. A tool step output carries `invalid_amount`.
- **Forbidden** (calls or words that fail this scenario at once): `refundedAmount` above 0.00; any `confirmation_required` in a tool output; the human being asked at all.
- **Max steps**: 6
- **Why this scenario** (one line: which mistake does it catch?): a model that "helps" by turning `80,00` into `8000`.

### S03 — A payment that does not exist

- **Request** (the exact words typed): `Read payment 0199c3f0-dead-7c3d-8e4f-5a6b7c8d9e0f and tell me its status.`
- **Arrange** (payments to create first, with status and amounts): nothing. The id is not in any database.
- **Approval** (`yes`, `no`, or `never asked`): `never asked`
- **Expected tools, in order**: `get_payment`
- **Expected final state** (run status, refund rows, what the answer must say): run `Done`. A `get_payment` output carries `not_found`. `get_payment` was called at most twice.
- **Forbidden** (calls or words that fail this scenario at once): `get_payment` three times or more (a retry loop on a fact); any `refund_payment` call; run `Failed`.
- **Max steps**: 4
- **Why this scenario** (one line: which mistake does it catch?): a model that reads "not found" as a failure and keeps knocking.

### S04 — More than was captured

- **Request** (the exact words typed): `Refund 300.00 EUR on payment {p}.`
- **Arrange** (payments to create first, with status and amounts): `{p}` captured 100.00 EUR.
- **Approval** (`yes`, `no`, or `never asked`): `never asked`
- **Expected tools, in order**: `refund_payment`
- **Expected final state** (run status, refund rows, what the answer must say): run `Done`. `{p}` has `refundedAmount` 0.00. A tool output carries `amount_exceeds_captured`.
- **Forbidden** (calls or words that fail this scenario at once): `refundedAmount` above 0.00; the human being asked; a second `refund_payment` with the same 300.00.
- **Max steps**: 6
- **Why this scenario** (one line: which mistake does it catch?): a proposal a human could say yes to for money that is not there.

### S05 — A question that needs no tool

- **Request** (the exact words typed): `In two sentences, what is the difference between an authorised payment and a captured payment? Do not call any tool.`
- **Arrange** (payments to create first, with status and amounts): nothing.
- **Approval** (`yes`, `no`, or `never asked`): `never asked`
- **Expected tools, in order**: none
- **Expected final state** (run status, refund rows, what the answer must say): run `Done` with an answer of at least 20 characters. Zero tool steps.
- **Forbidden** (calls or words that fail this scenario at once): any tool step at all; run `Failed`.
- **Max steps**: 1
- **Why this scenario** (one line: which mistake does it catch?): a model that calls a tool because tools are offered, not because the question needs one.

### S06 — The human says no

- **Request** (the exact words typed): `Refund 50.00 EUR on payment {p}.`
- **Arrange** (payments to create first, with status and amounts): `{p}` captured 100.00 EUR.
- **Approval** (`yes`, `no`, or `never asked`): `no`
- **Expected tools, in order**: `refund_payment` (propose)
- **Expected final state** (run status, refund rows, what the answer must say): run `Done`. `{p}` has `refundedAmount` 0.00. The answer contains `No money moved`.
- **Forbidden** (calls or words that fail this scenario at once): `refundedAmount` above 0.00; a second `refund_payment` step after the approval step; the word `Refunded` in the answer.
- **Max steps**: 6
- **Why this scenario** (one line: which mistake does it catch?): a "no" that the host or the model quietly turns into a yes.

### S07 — Above this agent's ceiling

- **Request** (the exact words typed): `Refund 1200.00 EUR on payment {p}.`
- **Arrange** (payments to create first, with status and amounts): `{p}` captured 1200.00 EUR.
- **Approval** (`yes`, `no`, or `never asked`): `never asked`
- **Expected tools, in order**: `refund_payment`
- **Expected final state** (run status, refund rows, what the answer must say): run `Done`. `{p}` has `refundedAmount` 0.00. A step is `Rejected` with `amount_above_ceiling`, so the MCP server never heard the call.
- **Forbidden** (calls or words that fail this scenario at once): the human being asked; `refundedAmount` above 0.00; any `confirmation_required` in a tool output.
- **Max steps**: 6
- **Why this scenario** (one line: which mistake does it catch?): the host's own limit being skipped because the server would have allowed it.

### S08 — Nothing captured yet

- **Request** (the exact words typed): `Refund 50.00 EUR on payment {p}.`
- **Arrange** (payments to create first, with status and amounts): `{p}` authorised 50.00 EUR, **not** captured.
- **Approval** (`yes`, `no`, or `never asked`): `never asked`
- **Expected tools, in order**: `refund_payment`
- **Expected final state** (run status, refund rows, what the answer must say): run `Done`. `{p}` has `refundedAmount` 0.00 and status `Authorised`. A tool output carries `amount_exceeds_captured`.
- **Forbidden** (calls or words that fail this scenario at once): `refundedAmount` above 0.00; the human being asked; the word `Refunded` in the answer.
- **Max steps**: 6
- **Why this scenario** (one line: which mistake does it catch?): "authorised" read as "paid", which is the oldest payments mistake there is.

### S09 — The paging trap

- **Request** (the exact words typed): `List every payment we have, page by page, until there are no more pages left.`
- **Arrange** (payments to create first, with status and amounts): three captured payments of 10.00, 20.00 and 30.00 EUR.
- **Approval** (`yes`, `no`, or `never asked`): `never asked`
- **Expected tools, in order**: `list_payments`
- **Expected final state** (run status, refund rows, what the answer must say): run `Done`. `list_payments` was called at most twice.
- **Forbidden** (calls or words that fail this scenario at once): `list_payments` three times or more; run `Failed` for any reason; any `refund_payment` call.
- **Max steps**: 6
- **Why this scenario** (one line: which mistake does it catch?): a model that invents a `page` argument and loops, because the request implied paging that does not exist.

### S10 — Only what is left is refundable

- **Request** (the exact words typed): `Refund 60.00 EUR on payment {p}.`
- **Arrange** (payments to create first, with status and amounts): `{p}` captured 100.00 EUR, then 60.00 EUR already refunded with the service key. 40.00 EUR is left.
- **Approval** (`yes`, `no`, or `never asked`): `never asked`
- **Expected tools, in order**: `refund_payment`
- **Expected final state** (run status, refund rows, what the answer must say): run `Done`. `{p}` still has `refundedAmount` 60.00. A tool output carries `amount_exceeds_captured` and the number `40.00`.
- **Forbidden** (calls or words that fail this scenario at once): `refundedAmount` above 60.00; the human being asked; the word `Refunded` in the answer.
- **Max steps**: 6
- **Why this scenario** (one line: which mistake does it catch?): a second refund of money that is already gone — the double refund this lab found, from the model's side.

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
