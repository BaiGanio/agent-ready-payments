# Lab 00 — Interview Q&A: payments words, setup, Gitflow

How to use: read the question, answer out loud, then check. A **Trap** is the follow-up a sharp interviewer asks next.

Typical context: a payments platform with its **own acquiring**, online + in-store acceptance. So expect acquiring-side vocabulary.

---

## A. The parties

**Q1. Who are the four parties in a card payment?**
A: Cardholder (pays), merchant (sells), **issuer** (the cardholder's bank, issues the card, approves or declines), **acquirer** (the merchant's bank, takes the money in for the merchant). In between sit the **card schemes** (Visa, Mastercard), which route messages and set rules.
Trap: "Where does a gateway / PSP sit?" → A gateway or PSP connects the merchant to one or more acquirers. Many companies are both: a platform (gateway/PSP role) and an acquirer.

**Q2. Issuer vs acquirer — one sentence each.**
A: The issuer decides *if* the cardholder can pay. The acquirer guarantees the merchant *gets paid* and carries the merchant's risk.

**Q3. What is a merchant acquirer's main risk?**
A: The merchant. If the merchant goes bust or commits fraud, chargebacks come back to the acquirer. That is why acquirers do underwriting, hold reserves and watch chargeback ratios.

**Q4. What is a MID?**
A: Merchant ID — the identifier an acquirer gives a merchant account. One merchant can have many MIDs (per country, currency, or channel).

## B. The payment lifecycle

**Q5. Authorisation vs capture?**
A: **Authorisation** asks the issuer to reserve funds; no money moves yet. **Capture** tells the acquirer "take the money now". Hotels and e-commerce authorise at order time and capture at check-out / shipping.
Trap: "Can you capture more than you authorised?" → Normally no. Some sectors (hospitality, tips) allow a small over-capture or use an **incremental authorisation**. Default rule in our model: capture ≤ authorised amount.

**Q6. What is a sale / auth-capture in one step?**
A: Authorisation and capture in one message. Used when goods ship at once (digital goods, POS).

**Q7. Void vs refund?**
A: **Void** cancels an authorisation *before* capture/settlement — no money moved, the hold is released. **Refund** returns money *after* capture — a new money movement in the other direction.
Trap: "Can you void after capture?" → Some processors allow void until the batch settles (same day). After settlement, only a refund.

**Q8. Partial capture, partial refund?**
A: Capture less than authorised (ship part of an order). Refund less than captured. The sum of refunds must never be more than the captured amount. That rule is a test in Lab 01.

**Q9. What is settlement?**
A: The step where the money actually moves. Captured transactions are grouped in a **batch**, sent to the scheme, netted, and the acquirer pays the merchant (minus fees). Usually T+1 or T+2.

**Q10. What is clearing vs settlement?**
A: **Clearing** = exchanging the transaction details and computing who owes whom. **Settlement** = the funds transfer itself.

**Q11. What is a chargeback?**
A: The cardholder disputes a charge with the issuer. The issuer pulls the money back from the acquirer, who pulls it from the merchant. The merchant can fight it (**representment**) with evidence.
Trap: "Refund vs chargeback?" → A refund is the merchant's choice. A chargeback is forced by the issuer and costs a fee and hurts the merchant's ratio.

**Q12. What is interchange? MDR?**
A: **Interchange** = fee the acquirer pays the issuer per transaction. **MDR** (merchant discount rate) = total fee the merchant pays the acquirer (interchange + scheme fees + acquirer margin).

## C. Security and regulation

**Q13. What is 3DS?**
A: 3-D Secure (3DS2 today). An extra cardholder check during online payment (bank app, OTP, biometrics). It moves fraud liability from the merchant to the issuer. In the EU, it is how **SCA** (Strong Customer Authentication, PSD2) is done.
Trap: "Frictionless flow?" → The issuer approves on risk data alone, no challenge shown to the cardholder.

**Q14. What is PCI DSS and how does it affect a backend developer?**
A: The card-data security standard. For a developer: never log a PAN or CVV, never store CVV at all, store PAN only encrypted or as a **token**, keep card data in a small "cardholder data environment" so the rest of the system stays out of scope.

**Q15. What is tokenisation?**
A: Replacing the card number with a token that is useless outside the system. Network tokens (from Visa/Mastercard) also survive card re-issue.

**Q16. What is AML / KYC in one line each?**
A: KYC = know who your merchant/customer is. AML = detect and report money laundering. Both drive merchant onboarding checks at an acquirer.

## D. Engineering words that matter in payments

**Q17. What is idempotency and why does a payment API need it?**
A: Doing the same request twice has the same effect as doing it once. Networks time out; clients retry. Without idempotency a retry can charge twice. Covered deeply in Lab 03.

**Q18. Why never use `double` for money?**
A: Binary floating point cannot represent 0.10 exactly; sums drift. Use `decimal` in C# and `DECIMAL(19,4)` (or integer minor units) in SQL. Always store the **currency** next to the amount.
Trap: "Minor units?" → Store 1050 + "EUR" for €10.50. Careful: JPY has 0 decimals, some currencies (BHD, KWD) have 3.

**Q19. What is reconciliation?**
A: Matching your own records against the acquirer/scheme settlement files, line by line, and explaining every difference. A daily job in every payments company.

**Q20. What does "exactly once" mean in payments, honestly?**
A: There is no exactly-once delivery over a network. You get **at-least-once delivery + idempotent processing**, which gives exactly-once *effect*.

## E. Setup and Gitflow

**Q21. Explain Gitflow.**
A: `main` holds released code, tagged. `develop` is the integration branch. `feature/*` branch from `develop` and merge back. `release/*` branch from `develop` for stabilisation, then merge into `main` (tag) and back into `develop`. `hotfix/*` branch from `main`, merge into both.
Trap: "Gitflow vs trunk-based?" → Gitflow fits scheduled releases and several supported versions (common in regulated payments). Trunk-based fits continuous deployment with feature flags. Know both; follow the team.

**Q22. Why `--no-ff` merges in Gitflow?**
A: A merge commit keeps the feature visible as one unit in history. Easy to see and easy to revert.

**Q23. Why does SQL Server run slowly on your Mac?**
A: The official image is amd64 only. On Apple Silicon Docker runs it under Rosetta emulation. Fine for labs, never for performance numbers.

**Q24. Why two databases in the labs?**
A: Postgres runs natively on ARM (Mac, Pi). SQL Server is the target engine — stored procedures and locking work must be done on the real engine, because locking behaviour is engine-specific.

## F. The lab database, migrations and test data

**Q25. How do you manage database schema changes?**
A: With versioned migration scripts in source control. Each script runs once, in order, and a journal table records it. I used **DbUp** with plain `.sql` files. You review every line of SQL, and stored procedures live next to the tables. EF Core migrations are the alternative when EF owns the schema.

**Q26. DbUp vs EF Core migrations vs SSDT / DACPAC?**
A:
- **DbUp:** plain SQL scripts, run forward only. Simple, explicit, good for a stored-procedure world.
- **EF Core migrations:** generated from the C# model. Good when the app owns the schema.
- **SSDT / DACPAC:** you declare the target schema, and a tool computes the diff. This is common in big SQL Server shops. It is powerful, but a computed diff on a big table can be dangerous, so you review the generated script.

**Q27. How do you change a big table in production without downtime?**
A: In steps: expand, then contract. Add the new column as nullable. Fill it in batches. Deploy code that writes both the old and the new column. Switch reads to the new column. Remove the old column in a later release. Never do a long-locking change in one script on a hot payments table.

**Q28. Why seed with a fixed random seed?**
A: The same data comes out every time, so a failing test or demo can be repeated exactly. Random data with no seed gives tests that fail only sometimes, and you can't reproduce them.

**Q29. How do you keep integration tests independent?**
A: Each test arranges its own data and never depends on demo data or on test order. The database is reset between tests (Respawn deletes rows but keeps the schema). Postgres runs in a throwaway Testcontainers container. On SQL Server, each test class gets its own database.

**Q30. Why two database engines with the same tables?**
A: Postgres runs natively on ARM, so it is fast on the Mac. SQL Server is the target engine, and locking behaviour is engine-specific. Keeping the shared tables in lockstep in both is a real discipline. Aperio does the same with Postgres and SQLite (14 migrations per engine).

**Q31. Would you put real customer data in a test database?**
A: No. Use generated data, or masked copies with card numbers removed or replaced by tokens. PCI DSS forbids real card data in test environments, and GDPR limits personal data.


## Aperio: have you solved this before?

**Q-APERIO. "Have you solved this before?"**
A: I have set up containers, migrations kept in lockstep across two engines, and a release process (SemVer, changelog, release workflow) in Aperio. Aperio uses type-prefixed feature branches on `master`, not full Gitflow, so this repo is where I run real Gitflow. Its `normalizeAmount()` already refuses to guess a decimal separator and requires a currency — the same money discipline payments need.
Details and file references: the "Aperio comparison" table in this lab's `README.md`.
