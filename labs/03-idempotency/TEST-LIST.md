# Lab 03 — Test list

Written by the agent: **the names and the rules**. Written by **you**: every body.

That split is the rule in `CLAUDE.md`. The name is yours to improve. The **rule** in the right column is not negotiable, and it is what the step's Proof checks.

How to use it:

```bash
grep -n '^###' labs/03-idempotency/TEST-LIST.md                    # the groups
sed -n '/^### ReplayTests/,/^### /p' labs/03-idempotency/TEST-LIST.md
```

One group is one file in `tests/Payments.Api.Tests`. Every group except `IdempotencyStoreTests` talks to the API over `HttpClient`.

**Counts.** 65 tests in 12 groups: 20 ported from the lab 02 list (commands), 8 from `CF-020` (validation), 37 new (idempotency). A guide, not a target.

**What was really built (2026-10-22).** 109 tests in `Payments.Api.Tests`, all green. Two changes from the list:

- `ConcurrencyTests` (3) was **not written** — step 13 skipped, `CF-035`. Nothing in the suite covers the race.
- One test came in from lab 08's list instead: `A_dropped_answer_still_refunds_once`, in `McpEndToEndTests`, with the `DropFirstAnswer` handler. It needed this lab's replay (`CF-032`).

**Helpers send a key from the start (step 3).** From step 10 on, every POST needs an `Idempotency-Key`. So in step 3, before any command test exists, every POST helper in `ApiTestBase` sends one: `CreatePaymentAsync` and a new `PostAsync(path, body, key)` both default to a fresh `Guid.NewGuid().ToString()`, and take a `key` argument so an idempotency test can pass its own. Until step 10 the server ignores the header. After step 10 nothing breaks. That is a change to a helper, required by the new contract — not a test edited to go green.

---

### ValidationTests (8) — step 2

Ported from the lab 02 list. Closes `CF-020`. These test the **existing** built-in validation, so they go green at once. That is fine: they pin today's behaviour before step 9 adds a header check in front of it.

| Test | The rule it must check |
|---|---|
| `A_missing_currency_is_400_naming_the_field` | `400`, and `errors` has a `Currency` entry |
| `A_three_letter_lower_case_currency_is_400` | `"eur"` is refused. The domain demands upper case; the edge says so first |
| `A_four_letter_currency_is_400` | `"EURO"` is refused by the regular expression |
| `A_zero_amount_is_400` | `0` fails the range at the edge, so it never reaches `Money.Of` |
| `A_negative_amount_is_400` | `-5` is refused with the field named |
| `An_empty_body_is_400` | No body at all is a `400`, never a `500` |
| `Malformed_json_is_400` | `{"amount":` is a `400` from the binder, and the body is still ProblemDetails |
| `A_validation_failure_writes_no_row` | After any `400`, `select count(*) from payment` is unchanged |

### AuthoriseTests (4) — step 3

| Test | The rule it must check |
|---|---|
| `Authorising_a_created_payment_is_200_and_Authorised` | `status` becomes `"Authorised"` |
| `An_authorised_payment_allows_capture_and_void` | `allowedActions` is exactly `["Capture","Void"]`. The domain allows void even though this lab has no void endpoint |
| `An_authorised_payment_can_capture_its_full_amount` | `capturableRemaining` equals `authorisedAmount` |
| `Authorising_twice_is_409_illegal_transition` | Two **different** keys: `409`, `code` is `illegal_transition`, and the second call wrote no operation row |

### CaptureTests (8) — step 3

| Test | The rule it must check |
|---|---|
| `Capturing_the_full_amount_is_200_and_Captured` | `status` is `"Captured"`, `capturedAmount` equals `authorisedAmount` |
| `Capturing_part_is_200_and_PartiallyCaptured` | 40 of 100 leaves `status` `"PartiallyCaptured"` and `capturableRemaining` `60.00` |
| `Capturing_exactly_what_is_left_is_allowed` | The limit is inclusive. 60 after 40 of 100 succeeds and ends `"Captured"` |
| `Capturing_more_than_authorised_is_422_amount_exceeds_authorised` | `422`, that `code`, and `capturedAmount` did not move |
| `Capturing_a_payment_that_was_never_authorised_is_409` | State first: on a `Created` payment it is `409 illegal_transition`, not a `422` about the amount. (Lab 02 used a voided payment; there is no void endpoint here) |
| `Capturing_in_another_currency_is_422_currency_mismatch` | `USD` on a `EUR` payment is `422 currency_mismatch` |
| `A_refused_capture_writes_no_operation_row` | After any `409` or `422`, the operation count in the database is unchanged |
| `Two_captures_add_up` | 30 then 30, two different keys: `capturedAmount` `60.00` and two operation rows |

### RefundTests (8) — step 3

| Test | The rule it must check |
|---|---|
| `Refunding_a_captured_payment_is_200_and_Refunded` | Full refund ends in `"Refunded"`, `allowedActions` empty |
| `Refunding_part_is_200_and_PartiallyRefunded` | 60 of 100 leaves `"PartiallyRefunded"` and `refundableRemaining` `40.00` |
| `A_refund_is_limited_by_what_was_captured` | Authorised 100, captured 40: a 50 refund is `422 amount_exceeds_captured`, and `refundableRemaining` read back is `40.00` |
| `Refunding_everything_captured_on_a_partial_capture_ends_Refunded` | Captured 40 of 100, refunded 40 → `"Refunded"`. The uncaptured hold is never claimed |
| `Refunding_an_authorised_payment_is_409` | Nothing was captured, so there is nothing to give back: `409 illegal_transition` |
| `Refunding_twice_over_the_total_is_422` | 60 then 60 of a 100 capture, two different keys: the second is `422 amount_exceeds_captured` |
| `Refunding_in_another_currency_is_422_currency_mismatch` | Currency is checked before the amount |
| `The_error_body_is_problem_json_with_a_code` | `Content-Type` is `application/problem+json`; `type` is `urn:payments-labs:error:<code>`; `code` matches; `detail` holds no stack trace and no word `Exception` |

### IdempotencyStoreTests (7) — step 7

The only new group that may call a store directly. What it checks is the store's own contract, which no HTTP response can show.

| Test | The rule it must check |
|---|---|
| `A_fresh_key_is_claimed` | `ClaimAsync` returns `Claimed`, and one row with status `InProgress` exists |
| `A_second_claim_of_an_open_key_is_in_progress` | Same merchant, key and hash, twice: the second returns `InProgress`, and there is still one row |
| `A_claim_with_another_hash_is_a_mismatch` | Same merchant and key, other hash: `Mismatch`. The stored hash is unchanged |
| `The_same_key_for_another_merchant_is_a_new_claim` | M1 and M2 both claim `"K"`: both `Claimed`, two rows |
| `A_completed_key_replays_its_stored_answer` | Claim, complete with `201`, a body and a location, claim again: `Replay` with the same three values |
| `A_released_key_can_be_claimed_again` | Claim, release, claim: `Claimed`, and one row |
| `The_completed_mark_rolls_back_with_the_payment_write` | Claim K. Call `InsertAsync` with a `Completion` for K and a payment whose id already exists, so the insert fails. The key is still `InProgress` and has no stored answer. This is the atomic phase |

### KeyHeaderTests (6) — step 9

| Test | The rule it must check |
|---|---|
| `A_missing_key_is_400_idempotency_key_required` | A `[Theory]` over the four POSTs: `400` and that `code` on each |
| `A_blank_key_is_400_idempotency_key_required` | `Idempotency-Key: "   "` counts as missing |
| `A_key_longer_than_255_is_400_idempotency_key_invalid` | 256 characters: `400`, that `code` |
| `A_key_of_exactly_255_is_accepted` | The limit is inclusive: `201` |
| `A_refused_key_writes_nothing` | After any of the above `400`s, `payment` and `idempotency_key` both have zero rows |
| `A_GET_needs_no_key` | `GET /payments/{id}` with no header is `200` |

### ReplayTests (7) — step 9

All on `POST /payments`.

| Test | The rule it must check |
|---|---|
| `The_same_key_twice_creates_one_payment` | Two creates, one key: both bodies carry the same `id`, and `payment` has one row |
| `A_replay_returns_the_same_status_and_body` | The second answer is `201`, and its body string is **equal** to the first one's |
| `A_replay_carries_the_Idempotent_Replayed_header` | The first answer has no such header; the second has `Idempotent-Replayed: true` |
| `A_replay_keeps_the_location_header` | Both answers have the same `Location` |
| `Two_different_keys_create_two_payments` | Same body, two keys: two ids, two rows |
| `The_same_key_from_two_merchants_creates_two_payments` | M1 and M2 send the same key string: two payments. The scope is the merchant |
| `A_replay_shows_the_original_answer_not_the_current_state` | Create with K, authorise it, create with K again: the replayed body still says `"Created"`. A replay is the stored answer, not a fresh read |

### KeyReuseTests (4) — step 9

| Test | The rule it must check |
|---|---|
| `The_same_key_with_another_amount_is_422_idempotency_key_reused` | Create 100 with K, then 200 with K: `422`, that `code` |
| `The_same_key_on_another_endpoint_is_422` | Capture with K, then refund the same payment with K: `422 idempotency_key_reused`. The path is in the hash |
| `The_same_key_on_another_payment_is_422` | Refund payment A with K, then refund payment B (same merchant) with K: `422` |
| `A_reused_key_changes_nothing` | After the `422`, row counts are unchanged and a third call with the **first** body still replays the first answer |

### CommandReplayTests (5) — step 11

| Test | The rule it must check |
|---|---|
| `Refunding_twice_with_one_key_refunds_once` | Two refunds of 40, one key: `refundedAmount` is `40.00`, not `80.00`, and there is one `Refund` row |
| `Capturing_twice_with_one_key_captures_once` | The same, for capture |
| `Authorising_twice_with_one_key_is_200_twice` | Same key: `200` and `200`, the second replayed. Compare with `Authorising_twice_is_409_illegal_transition` — that one uses two keys |
| `A_refused_command_is_stored_and_replayed` | Refund 500 on a 100 capture with K: `422 amount_exceeds_captured`. Again with K: the same `422` body, with `Idempotent-Replayed: true` |
| `A_404_is_not_stored` | Refund an unknown id with K: `404 payment_not_found`, and `idempotency_key` has zero rows |

### InProgressTests (3) — step 11

Arrange the open key by hand with `ExecuteSqlAsync`: an `InProgress` row whose `request_hash` comes from `RequestHash.Of(…)` for the request you then send. No timing, no threads — a race test that depends on luck belongs in `ConcurrencyTests`.

| Test | The rule it must check |
|---|---|
| `An_open_key_is_409_idempotency_request_in_progress` | `409` and that `code` |
| `The_409_says_when_to_retry` | The answer has `Retry-After: 1` |
| `An_open_key_runs_no_work` | `payment` (for create) or `payment_operation` (for refund) is unchanged |

### FailureTests (2) — step 11

Replace `IPaymentStore` with a test double that throws on `InsertAsync`, through `Fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(…))`. The double lives in the test project.

| Test | The rule it must check |
|---|---|
| `A_crash_during_the_work_releases_the_key` | The create is a `500`, and `idempotency_key` has zero rows afterwards. A `5xx` is never stored |
| `A_retry_after_a_crash_does_the_work` | Then the same key through the normal client: `201`, one payment |

### ConcurrencyTests (3) — step 13

Fire the requests with `Task.WhenAll` over one `HttpClient`. Build all 50 request messages first, then send, so the start is as close to the same instant as you can make it.

| Test | The rule it must check |
|---|---|
| `Fifty_parallel_creates_with_one_key_make_one_payment` | Exactly one `payment` row. Every `201` body has the same `id`. Every other answer is `409 idempotency_request_in_progress`. Nothing else — no `500`, no `422` |
| `Fifty_parallel_refunds_with_one_key_refund_once` | On a captured payment: exactly one `Refund` operation row, and `refundedAmount` read back is the one refund |
| `After_the_storm_a_retry_replays_the_winner` | After the 50 creates, one more with the same key: `201`, the winner's `id`, `Idempotent-Replayed: true` |

---

## Self-check before you call the list done

- [ ] Every test asserts something. A test with no `Assert` is a lie told in green.
- [ ] Every test arranges its own data and its own keys. No key is shared between tests.
- [ ] Every failure test also checks that **nothing changed** in the database — `payment`, `payment_operation` **and** `idempotency_key`.
- [ ] Every ProblemDetails test checks the `code`, not the `detail` text.
- [ ] `ConcurrencyTests` went red once with the claim broken on purpose, then green.
- [ ] No test sleeps to "wait for" the other request. A sleep is a guess.
- [ ] No real card numbers. Not even in a key.
