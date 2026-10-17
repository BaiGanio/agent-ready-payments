# Lab 19 — The human in the loop: an approval screen *(optional)*

## What we solve
Labs 08, 09 and 13 all say "a human approves". This lab builds that human's screen. It shows what the agent wants to do, with enough context to judge it. One click approves or rejects. The screen can never approve something different from what the person saw.

## Simple example
The agent proposes: *"Refund 80.00 EUR on payment 42 (version 7). Reason: duplicate charge, same card, same amount, 40 seconds apart."*

The support person opens **Pending approvals** and sees one card:
- **What:** refund 80.00 EUR on payment 42.
- **Why:** the agent's reason, plus the evidence it used (the two payments side by side).
- **Who:** proposed by agent `support-bot` (model gemma-4-E4B), 3 minutes ago. Expires in 2 minutes.
- **Buttons:** *Approve* · *Reject (with reason)*.

Now three things can go wrong, and the screen must handle each one:
1. Someone captures more on payment 42 first, so it is now version 8. *Approve* → `412 stale_version` → the card says *"This payment changed after the proposal. Ask the agent to propose again."* No refund happens.
2. The token expires while the person reads. → The card greys out with *"Expired"*. *Approve* is disabled.
3. The person double-clicks *Approve*. → One refund (idempotency, Lab 03). The second click shows the first result.

## Why it matters for a payment platform
The job title says **Full Stack**. Every agent design that touches money needs a human gate. A gate is only as good as the screen behind it. If the screen hides the amount, or lets a person approve a stale version, the gate is fake. This is also where Lab 09's rule ("model output cannot approve money") becomes real: the only *Approve* button sits here, behind a person's login.

## Everyday picture
**The bank teller's second signature.** A junior clerk (the agent) fills in a transfer form. The senior teller (the human) gets the form, not a summary of a summary. She sees the amount, the account and why. She signs **that form**. If someone changes the amount after she signed, her signature does not count for the new amount. She cannot sign forms that she filled in herself (the four-eyes rule: two different people).

## Reuse from `scrooge-financing`
My earlier Svelte app ([BaiGanio/scrooge-financing](https://github.com/BaiGanio/scrooge-financing)) already has the shape: a list, a detail card, *Approve* / *Reject with reason*, and a "to confirm" display for missing fields. **Reuse the patterns, not the files.** That app shows financing requests, not refunds.
- `src/lib/api.ts`: one store as the single source of truth, a typed `loading | loaded | error` state, and a refresh after every action.
- Component tests with Vitest + Testing Library and a mocked `fetch`.
- The GitHub Pages deploy workflow and the base-URL / CORS notes in `DECOUPLING-PLAN.md`.
- Upgrade on the way: Svelte 4 → Svelte 5.

## The shape (kept small)
- **API (C#):** `GET /approvals?status=pending` (keyset paged, Lab 02), `GET /approvals/{token}`, `POST /approvals/{token}/approve`, `POST /approvals/{token}/reject` with `{ reason }`. The approve call sends `If-Match` with the version that the person saw (Lab 04).
- **Auth:** the person logs in. The API checks the scope `payments:approve`. An **agent key can never call approve** (`403`). A person cannot approve a proposal that they started themselves (four-eyes).
- **UI:** one list, one card, two buttons. A countdown to expiry. Clear states for `stale_version`, `expired`, `already_decided` and `forbidden`, each from the ProblemDetails `code`.
- **Lab 13 link:** the chargeback *Submit / Accept loss* decision uses the same card with different buttons.
- **Audit:** the approval row records the person, the time, the version and the token. Lab 14 links it to the agent's trace.

## Aperio comparison (our backbone)
| Case | Aperio | Where (verified 2026-10-17) | What this lab adds |
|---|---|---|---|
| Approval card in the UI | ✅ Has | `public/scripts/streaming/interrupts.js` (`_renderActionConfirmButton`: Confirm / Edit / Reject with reason) | Same idea for refunds, in Svelte |
| Pending approvals survive a page reload | ✅ Has | `_renderPendingInterrupts` restores cards from the server (`data-restored`) | `GET /approvals?status=pending` |
| Approve sends only the id, server re-checks | ✅ Has | `interrupt_decision` / `confirm_action` over WebSocket; the server re-validates and checks the digest | `If-Match` with the version the person saw |
| Edit the arguments before approving | ✅ Has | `allowedDecisions` includes `edit` | **Removed for money.** An edited refund is a new proposal, not an approval |
| Separate approver identity, four-eyes, approve scope | ❌ No (single-user app) | — | Person login, `payments:approve` scope, "not your own proposal" rule |

## Done when
- The pending list and the card work against the real API. Component tests cover every state: pending, approved, rejected, expired, stale, forbidden.
- **Stale:** the payment changes after the proposal → *Approve* gets `412`, and the UI says so. No refund.
- **Expired:** after expiry, *Approve* is disabled, and the API refuses it too. Never trust the UI alone.
- **Double click:** exactly one refund.
- **Agent key on approve:** `403`. **Same person who proposed:** `403 four_eyes_required`.
- An end-to-end run: agent proposes (Lab 09) → card appears → person approves → one refund → one audit row with the trace id.

Spec + failing tests: written when the lab starts.
