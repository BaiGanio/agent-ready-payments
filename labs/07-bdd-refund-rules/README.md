# Lab 07 — BDD: refund rules in Gherkin

## What we solve
Business rules written so that product, QA and developers read the **same** text — and that text runs as a test.

## Simple example
The product owner says: "You can't refund more than was captured." In BDD that becomes:

```gherkin
Given a payment captured for 100.00 EUR
And a refund of 60.00 EUR has been made
When the merchant refunds 50.00 EUR
Then the refund is rejected with "amount_exceeds_captured"
```

The product owner can read it. The CI runs it against the real API. If someone breaks the rule, this scenario goes red.

## Why it matters for a payment platform
The ad asks for TDD **and** BDD, and names SpecFlow. We use **Reqnroll** (SpecFlow's successor, same syntax).

## Everyday picture
A **recipe card** that the owner, the chef and the food inspector all read the same way: "Given 2 eggs, when you whisk for 1 minute, then the mix is fluffy." And the card can check itself: if someone changes the kitchen, the card goes red.

## Aperio comparison (our backbone)
| Case | Aperio | Where (verified 2026-10-17) | What this lab adds |
|---|---|---|---|
| Gherkin / `.feature` files | ❌ No | — | Reqnroll feature files |
| Scenario-based tests | ✅ Has | 14 JSON scenarios in `tests/harness/scenarios/`, each with an input, a scripted model (`providerScript`) and expectations (`expectedToolSequence`, `requiredAnswerTerms`) — Given/When/Then in JSON | The same thinking in business language |
| End-to-end / browser tests | ✅ Has | `tests/e2e/`, `tests/browser/`, `tests/playwright.config.js` | Steps against the real API |

## Done when
One `.feature` file with 5–8 refund scenarios runs green in `dotnet test`.

Spec + failing scenarios: written when the lab starts.
