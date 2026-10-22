# Lab 07 — Interview Q&A: BDD, Gherkin, SpecFlow / Reqnroll

---

**Q1. What is BDD?**
A: Behaviour-Driven Development: the team (product, QA, dev) agrees on behaviour as concrete examples in plain language *before* coding. The examples become automated tests. It is mainly a **communication** practice; the tooling is second.

**Q2. TDD vs BDD?**
A: TDD: developer-facing, small units, red-green-refactor. BDD: business-facing, behaviour of a feature, written in the domain's language, shared with non-developers. They work together: BDD scenarios on the outside, TDD inside.

**Q3. Gherkin keywords?**
A: `Feature`, `Rule`, `Background`, `Scenario`, `Scenario Outline` + `Examples`, `Given` (context), `When` (action), `Then` (outcome), `And`/`But`.

**Q4. Write a scenario for a refund rule.**
A:
```gherkin
Rule: Total refunds can never exceed the captured amount

  Scenario: Refund above captured amount is rejected
    Given a payment captured for 100.00 EUR
    And a refund of 60.00 EUR has been made
    When the merchant refunds 50.00 EUR
    Then the refund is rejected with "amount_exceeds_captured"
    And the payment's refunded total is still 60.00 EUR
```

**Q5. What makes a good scenario?**
A: Declarative, not imperative: "Given a captured payment", not "click … type …". One behaviour per scenario. Domain words, not UI or HTTP words. Concrete example values.

**Q6. What are the "Three Amigos"?**
A: Product owner, developer, tester meeting to discuss a story through examples before work starts. Output: the scenarios.

**Q7. Example mapping?**
A: A short workshop: yellow card = story, blue = rules, green = examples, red = questions. Red cards show what is unclear before coding.

**Q8. SpecFlow vs Reqnroll?**
A: SpecFlow was the standard .NET BDD tool; Tricentis ended it (end of life around 2024–2025). **Reqnroll** is the community fork by SpecFlow's original author: same Gherkin, same binding attributes, migration is mostly a namespace and package change. I used Reqnroll; I can work in SpecFlow codebases.

**Q9. How do step definitions work?**
A: Methods with `[Given]`/`[When]`/`[Then]` attributes and a pattern (Cucumber expressions or regex). Parameters are parsed from the text. Shared state between steps via **context injection** (constructor-injected classes) or `ScenarioContext`.

**Q10. Context injection vs `ScenarioContext`?**
A: Context injection: typed POCOs, clear, refactor-safe. `ScenarioContext["key"]`: stringly-typed dictionary. Prefer injection.

**Q11. Hooks?**
A: `[BeforeScenario]`, `[AfterScenario]`, `[BeforeTestRun]`, tags-filtered hooks (`[BeforeScenario("db")]`). Used to start the API (`WebApplicationFactory`) and reset the DB.

**Q12. At what level did your scenarios run?**
A: Against the running API over HTTP (in memory via `WebApplicationFactory`) with a real Postgres. That tests the real contract. Some teams run them against the domain directly for speed — fine, as long as the steps stay business-level.

**Q13. `Scenario Outline` — when?**
A: Same rule, several data rows. Don't overuse: a 40-row table hides intent. Big data sets belong in unit tests.

**Q14. Common BDD anti-patterns?**
A: UI-scripted steps, scenarios written by devs alone after the code, huge step libraries with near-duplicate phrases, testing every edge case in Gherkin (slow and noisy).

**Q15. How do BDD scenarios fit the test pyramid?**
A: Few, at the top-middle: key business rules end to end. Many unit tests below them. Keep the suite fast enough to run on every PR.

**Q16. Living documentation?**
A: Because scenarios run in CI, they can't drift from the code. Reports (Reqnroll LivingDoc-style or HTML) give product a current spec.

**Q17. How do you BDD-test something nondeterministic, like an agent?**
A: Pin the nondeterministic part and test the rest. Aperio does exactly this: `tests/harness/` replays a scripted model output through the *real* agent loop — deterministic, ~0.7 s, in CI — while a separate live benchmark (`scripts/model-tier-bench.js`) checks real models manually. Given scripted model output / When the loop runs / Then the destructive call is refused — that is a BDD scenario.

**Q18. Honest gap?**
A: Before this lab I had no Gherkin in production. Aperio has unit, integration, e2e and a scenario-based harness (JSON scenarios), but no feature files. This lab is where I practised the tooling; the scenario thinking I already used.


## Aperio: have you solved this before?

**Q-APERIO. "Have you solved this before?"**
A: Not with Gherkin. But Aperio's harness is scenario-driven: each JSON scenario is a Given (user message + scripted model), a When (the real loop runs) and a Then (expected tool sequence, required answer terms). This lab moves that thinking into Reqnroll feature files that a product owner can read.
Details and file references: the "Aperio comparison" table in this lab's `README.md`.
