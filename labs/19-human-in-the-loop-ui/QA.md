# Lab 19 — Interview Q&A: the human in the loop

How to use: read the question, answer out loud, then check. A **Trap** is the follow-up a sharp interviewer asks next.

---

## A. What the approver must see

**Q1. What goes on an approval card?**
A: The exact action (refund 80.00 EUR on payment 42), the reason, the evidence the agent used, who proposed it (agent id and model), when, and when it expires. The person approves the action itself, not a summary written by the model.
Trap: "Can the model write the summary?" → It can write the *reason*. The *action* line (amount, payment, version) comes from the proposal record in the database, never from model text.

**Q2. Why show the evidence and not just the reason?**
A: A reason is a claim. Evidence is checkable: the two payments side by side, same card, same amount, 40 seconds apart. If the person sees only "duplicate charge", they become a rubber stamp.

**Q3. What is "rubber-stamping", and how do you fight it?**
A: People approve without reading when approvals are many and almost always right. Fight it with: few approvals (only real money moves), clear evidence, a reason field on reject, and metrics on approval time. An approve rate of 100% in under 2 seconds means the gate is fake.

## B. Correctness: approving the right thing

**Q4. The payment changed after the proposal. What happens on Approve?**
A: The approve call sends `If-Match` with the version the person saw. The payment is now at a newer version, so the API answers `412 stale_version` and changes nothing. The card says the payment changed and asks for a new proposal.
Trap: "Why not just re-check the amount?" → The amount may still fit, but the context changed (a partial refund, a dispute hold). The person approved a picture of the world. If the picture is old, the approval is void.

**Q5. The token expired while the person was reading. Who enforces that?**
A: The API. The UI greys out the button as a kindness, but the API refuses an expired token on its own. Never trust the client for a rule about money.

**Q6. Double click on Approve?**
A: One refund. The approve call is idempotent on the token. The second call returns the first result. The UI also disables the button after one click, but the server is the real guard.

**Q7. Why remove "edit arguments" for money, when Aperio has it?**
A: An edited refund is a different action. If a person changes 80 to 60 and then approves, nobody proposed 60 and nobody else reviewed it. For money: reject with a reason, and let a new proposal go through the gate.

## C. Who may approve

**Q8. Can an agent call the approve endpoint?**
A: No. Approve needs the `payments:approve` scope. Only person logins have it. Agent API keys never have it, so an agent gets `403`. This is the one place where "the model said so" cannot turn into money.

**Q9. What is four-eyes?**
A: Two different people for one money action. The person who asked the agent for the refund cannot approve it. The API checks `approver != requester` and returns `403 four_eyes_required`.
Trap: "Small team, night shift?" → Then the policy says what is allowed (for example, under 50 EUR, one person). The code reads the policy. Nobody switches the check off in code.

**Q10. Where is the approval recorded?**
A: An approval row with the person, the time, the token, the version and the decision. It has the same trace id as the agent's run (Lab 14), so one trace shows proposal → approval → refund.

## D. Frontend engineering

**Q11. How do you model UI state?**
A: A typed union: `loading | loaded | error`, and per card `pending | approving | approved | rejected | expired | stale | forbidden`. Each state from the API comes from the ProblemDetails `code`, not from parsing a message. No boolean soup (`isLoading && !isError && …`).

**Q12. Polling, Server-Sent Events or WebSockets for new approvals?**
A: Start with polling every few seconds, with `If-None-Match` so an unchanged list is a cheap `304`. Move to Server-Sent Events when the list must be live. Events go one way, server to browser, which is all this needs. WebSockets only if the browser must also push a lot.

**Q13. How do you test the screen?**
A: Component tests with a mocked `fetch`: one test per state (pending, approved, stale, expired, forbidden), plus "the button is disabled after one click". The rules themselves are tested in the API, so the UI tests check display and wiring only.

**Q14. The UI is a static site on GitHub Pages. What breaks?**
A: Relative API paths (no backend at the same origin), the dev proxy (gone in a build), the base path (`/<repo>/`), and CORS (the API must allow the Pages origin). I wrote this down before, in `scrooge-financing/DECOUPLING-PLAN.md`.

**Q15. Where does the login token live in the browser?**
A: Best: an HttpOnly, Secure, SameSite cookie set by a small backend-for-frontend, so JavaScript cannot read it. `localStorage` is readable by any script on the page, so one XSS bug leaks it. For an approval screen that moves money, use the cookie.

## E. The bigger picture

**Q16. Why is the human gate a UI problem and not only an API problem?**
A: The API makes the gate *safe*: scopes, version, expiry, idempotency. The UI makes it *meaningful*. If the person cannot see what they approve, a safe gate still approves the wrong thing.

**Q17. One sentence for the interviewer?**
A: "The agent proposes, a person approves exactly what they saw, and the API refuses anything else: a stale version, an expired token, an agent key, or the requester approving their own refund."

## Aperio: have you solved this before?

**Q-APERIO. "Have you solved this before?"**
A: Yes, for one user. Aperio has an approval card with Confirm, Edit and Reject. Pending approvals survive a page reload, and the server re-validates before it acts. What it lacks is a separate approver: it is a single-user app, so there is no four-eyes rule and no approve scope. And for money I would remove Edit. This lab adds those, and my `scrooge-financing` Svelte app gives me the frontend patterns.
Details and file references: the "Aperio comparison" table in this lab's `README.md`.
