# Lab 01 — Interview Q&A: payment state machine, TDD

---

## A. The state machine

**Q1. Draw the payment states.**
A: `Created → Authorised → (PartiallyCaptured) → Captured → (PartiallyRefunded) → Refunded`. From `Authorised` you can also `Void → Voided`. From `Created`, a decline goes to `Declined`. Terminal: `Declined`, `Voided`, `Refunded`.
Trap: "Is `Expired` a state?" → Yes in real life: an authorisation that is never captured expires (often ~7 days for cards). Good extension.

**Q2. Why model it as an explicit state machine and not a few `bool` flags?**
A: Flags allow impossible combinations (`IsCaptured && IsVoided`). An explicit state plus a transition table makes illegal states unrepresentable and makes every rule testable in one place.

**Q3. How do you reject an illegal transition — exception or result?**
A: For expected business outcomes (refund more than captured, capture a voided payment) I return a **result type** (`Result<Payment, PaymentError>`), so callers must handle it. Exceptions stay for bugs and broken invariants. The API maps the error to a stable ProblemDetails code.
Trap: "Isn't that more code?" → Yes, a little. It buys determinism: the caller — or an agent — always gets a typed, named error instead of a 500.

**Q4. Where do you keep the amount rules?**
A: Inside the aggregate. `Capture(amount)` checks `amount ≤ authorised − captured`. `Refund(amount)` checks `amount ≤ captured − refunded`. The aggregate is the only place that changes those numbers.

**Q5. What is an aggregate here?**
A: The `Payment` with its operations list. It is the consistency boundary: all invariants about one payment are enforced inside it, and it is saved as one unit.

**Q6. How do you represent money?**
A: A `Money` value object: `decimal Amount` + `string Currency` (ISO 4217). Adding two different currencies throws. It is immutable (`record struct` or `record`).

**Q7. Which pattern is this? State pattern?**
A: A transition table (or a `switch` expression over `(state, command)`) is enough here. The GoF State pattern (one class per state) helps when each state has a lot of different behaviour. For ~7 states with small rules, the table is clearer and easier to test.

**Q8. How does the state machine help an AI agent calling your API?**
A: An agent can ask "what can I do next?" and get a deterministic answer: `AllowedActions` from the current state. It can never talk the system into an illegal move; the domain refuses it with a named reason.

**Q9. Where should the timestamp come from?**
A: Inject a clock (`TimeProvider` in .NET 8+). Never call `DateTime.UtcNow` inside the domain. Then tests are deterministic.

## B. TDD

**Q10. Describe TDD.**
A: Red → Green → Refactor. Write one failing test for the next small behaviour. Write the simplest code to pass. Clean up with tests green. Repeat.

**Q11. What did TDD change in your design in this lab?**
A: (Answer from your own experience after the lab.) Typical: it pushed me to a pure domain with no DB and no clock calls, because those were hard to test. It made errors into values because I needed to assert on them.

**Q12. What do you test first in a state machine?**
A: The happy path one step at a time (authorise, then capture), then each illegal transition, then the amount edges: exactly equal, one cent over, zero, negative.

**Q13. What is a good unit test name?**
A: It says behaviour, not method: `Refund_more_than_captured_is_rejected`. Readable as a spec.

**Q14. Arrange-Act-Assert — and why one Act?**
A: One act per test keeps the failure reason obvious. If a test needs two acts, it tests two behaviours.

**Q15. Theory / data-driven tests — when?**
A: When the same rule holds for many inputs: a table of `(fromState, command) → expected` covers the whole transition matrix in one test method (`[Theory]` + `[MemberData]` in xUnit, `[TestCase]` in NUnit).

**Q16. Mock or not in domain tests?**
A: No mocks. The domain is pure. Mocks belong at boundaries (clock, repository, HTTP), and even there a fake is often simpler.

**Q17. What is the difference between a stub, a mock, a fake, a spy?**
A: Stub returns canned answers. Mock verifies it was called in a certain way. Fake is a working light implementation (in-memory repository). Spy records calls for later asserts.

**Q18. 100% coverage — good target?**
A: No. Coverage shows what is *not* tested; it does not prove what is. For the domain I want every rule and every edge covered. For glue code, integration tests matter more.

**Q19. TDD vs test-after?**
A: Test-after tends to test the code you wrote. TDD tests the behaviour you wanted. TDD also stops over-building: you write only what a test asked for.

**Q20. When would you not do TDD?**
A: A spike to learn an unknown API. Throw it away and then TDD the real thing.

**Q21. How do you test a concurrency rule in a pure domain?**
A: You don't — the domain is single-threaded by design. Concurrency is handled at persistence: optimistic concurrency (version column) or a conditional `UPDATE … WHERE state = @expected`. That is Lab 04.


## Aperio: have you solved this before?

**Q-APERIO. "Have you solved this before?"**
A: Yes, the pattern. Aperio's agent interrupts are a database-backed state machine: a `CHECK` constraint lists the 9 allowed statuses, illegal decisions throw `InterruptConflictError`, and the clock is injected for tests. This lab applies the same idea to money, adds amount rules, and returns results instead of throwing.
Details and file references: the "Aperio comparison" table in this lab's `README.md`.
