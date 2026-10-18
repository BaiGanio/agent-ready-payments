# Lab 01 — TDD notes

Written by: **you**. The agent wrote the headings and the rules. It never writes the content.

These notes are the answer to `QA.md` **Q11** ("What did TDD change in your design in this lab?") and to **Q12**, **Q19** and **Q21**. An interviewer can hear in one sentence whether an answer comes from a book or from your hands, so write what actually happened, including the parts that went badly.

Rule for every line: it must name something **concrete** from this lab — a test name, a method, an error code, an edge you got wrong. "It improved my design" is not an answer.

Fill it in at the end of the lab, before you merge. 20 minutes.

---

## 1. What the tests forced on the design

Three things the test suite made you do that you would not have done by yourself.

-
-
-

## 2. The bug a test caught

The one failure that would have become a real bug. Name the test and what was wrong.

- Test:
- What my code did:
- Why it would have cost money in production:

## 3. The order I worked in

Why `Money` first and `AllowedActions` last. One or two sentences.

-

## 4. Red, green, refactor — what the refactor step actually gave me

Step 12 moved the transitions onto one table while the tests stayed green. What got better, and what would have stopped you doing it without tests?

-

## 5. Where I would not use TDD

One case from your own experience, not from the book.

-

## 6. What I would change about these tests

You will write the tests yourself from Lab 02. Name two things in `tests/Payments.Domain.Tests` you would do differently, and why.

-
-

---

## Self-check

Tick every line before you commit this file.

- [ ] Every bullet is filled in. No placeholder is left.
- [ ] Every bullet names a real test, method or error code from this lab.
- [ ] Nothing here is copied from `QA.md` or from the `README.md`.
- [ ] Section 2 names a bug of **yours**, not a general risk.
- [ ] I can say all of it out loud in under three minutes.
