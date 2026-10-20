# Lab 02 — Test list

Written by the agent: **the names and the rules**. Written by **you**: every body.

That split is the rule in `CLAUDE.md`. A test you read is a claim; a test you wrote is a decision about what the API promises. The name is yours to improve — the **rule** in the right column is not negotiable, and it is what the step's Proof checks.

How to use it:

```bash
grep -n '^###' labs/02-minimal-api-dapper/TEST-LIST.md          # the groups
sed -n '/^### RefundTests/,/^### /p' labs/02-minimal-api-dapper/TEST-LIST.md
```

One group is one file in `tests/Payments.Api.Tests`. Every group except `StoreTests` talks to the API over `HttpClient`; never call a handler or a store directly from an endpoint test, or you stop testing routing, binding, serialization and ProblemDetails.

**Counts.** Core: 47 tests in 8 groups. Stretch A: 9 more. Stretch B: doubles 12 of them. Totals are a guide, not a target — if you need two tests where the list says one, write two.

---

### HealthTests (1) — step 9

| Test | The rule it must check |
|---|---|
| `Health_is_ok` | `GET /health` is `200`. This test exists to prove the harness, not the API |

### CreatePaymentTests (6) — step 10

| Test | The rule it must check |
|---|---|
| `Creating_a_payment_is_201_with_a_location_header` | `201`, and `Location` is `/payments/{the id in the body}` |
| `A_new_payment_starts_in_status_Created` | `status` is `"Created"`, `capturedAmount` and `refundedAmount` are `0` |
| `A_new_payment_allows_authorise_and_decline` | `allowedActions` is exactly `["Authorise","Decline"]`, in that order |
| `A_new_payment_has_no_remaining_amounts_yet` | `capturableRemaining` and `refundableRemaining` are both `0` — nothing is authorised yet |
| `The_server_chooses_the_id` | Two creates with the same body return two different ids. A client cannot pick one |
| `An_unknown_merchant_is_404_merchant_not_found` | `404`, `code` is `merchant_not_found`, and no `payment` row was written |

### GetPaymentTests (4) — step 10

| Test | The rule it must check |
|---|---|
| `A_created_payment_can_be_read_back` | Every DTO field survives the round trip, amounts included, to the cent |
| `An_unknown_id_is_404_payment_not_found` | `404`, `code` is `payment_not_found` |
| `A_malformed_id_is_404_not_500` | `GET /payments/not-a-guid` does not match the `{id:guid}` route, so it is `404` |
| `The_read_body_is_application_json` | `Content-Type` is `application/json`, and a failure is `application/problem+json` |

### ValidationTests (8) — step 10

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

### AuthoriseTests (4) — step 11

| Test | The rule it must check |
|---|---|
| `Authorising_a_created_payment_is_200_and_Authorised` | `status` becomes `"Authorised"` |
| `An_authorised_payment_allows_capture_and_void` | `allowedActions` is exactly `["Capture","Void"]` |
| `An_authorised_payment_can_capture_its_full_amount` | `capturableRemaining` equals `authorisedAmount` |
| `Authorising_twice_is_409_illegal_transition` | `409`, `code` is `illegal_transition`, and the second call wrote no operation row |

### VoidTests (4) — step 11

| Test | The rule it must check |
|---|---|
| `Voiding_an_authorised_payment_is_200_and_Voided` | `status` becomes `"Voided"` |
| `A_voided_payment_allows_nothing` | `allowedActions` is empty. Voided is terminal |
| `Voiding_a_captured_payment_is_409` | `409 illegal_transition` — money has already moved |
| `Voiding_an_unknown_payment_is_404` | `404 payment_not_found`, checked before any state rule |

### CaptureTests (8) — step 12

| Test | The rule it must check |
|---|---|
| `Capturing_the_full_amount_is_200_and_Captured` | `status` is `"Captured"`, `capturedAmount` equals `authorisedAmount` |
| `Capturing_part_is_200_and_PartiallyCaptured` | 40 of 100 leaves `status` `"PartiallyCaptured"` and `capturableRemaining` `60.00` |
| `Capturing_exactly_what_is_left_is_allowed` | The limit is inclusive. 60 after 40 of 100 succeeds and ends `"Captured"` |
| `Capturing_more_than_authorised_is_422_amount_exceeds_authorised` | `422`, that `code`, and `capturedAmount` did not move |
| `Capturing_on_a_voided_payment_is_409` | State first: `409 illegal_transition`, not a `422` about the amount |
| `Capturing_in_another_currency_is_422_currency_mismatch` | `USD` on a `EUR` payment is `422 currency_mismatch` |
| `A_refused_capture_writes_no_operation_row` | After any `409` or `422`, the operation count in the database is unchanged |
| `Two_captures_add_up` | 30 then 30 gives `capturedAmount` `60.00` and two operation rows |

### RefundTests (8) — step 13

| Test | The rule it must check |
|---|---|
| `Refunding_a_captured_payment_is_200_and_Refunded` | Full refund ends in `"Refunded"`, `allowedActions` empty |
| `Refunding_part_is_200_and_PartiallyRefunded` | 60 of 100 leaves `"PartiallyRefunded"` and `refundableRemaining` `40.00` |
| `A_refund_is_limited_by_what_was_captured` | Authorised 100, captured 40: a 50 refund is `422 amount_exceeds_captured`, and the body's `refundableRemaining` says `40.00` |
| `Refunding_everything_captured_on_a_partial_capture_ends_Refunded` | Captured 40 of 100, refunded 40 → `"Refunded"`. The uncaptured hold is never claimed |
| `Refunding_an_authorised_payment_is_409` | Nothing was captured, so there is nothing to give back: `409 illegal_transition` |
| `Refunding_twice_over_the_total_is_422` | 60 then 60 of a 100 capture: the second is `422 amount_exceeds_captured` |
| `Refunding_in_another_currency_is_422_currency_mismatch` | Currency is checked before the amount |
| `The_error_body_is_problem_json_with_a_code` | `Content-Type` is `application/problem+json`; `type` is `urn:payments-labs:error:<code>`; `code` matches; and `detail` holds no stack trace and no word `Exception` |

### StoreTests (4) — step 13, doubled by stretch B

These are the only tests that may talk to the store directly, because what they check is the store's own contract.

| Test | The rule it must check |
|---|---|
| `A_round_trip_keeps_every_amount_and_timestamp` | Insert, update, then load: status, all three amounts, `createdAt` and every operation `At` come back unchanged. The `ReplayClock` is what makes the timestamps survive |
| `A_replayed_payment_matches_the_stored_status` | Hand-write a `payment` row whose `status` disagrees with its operation rows, then load: it throws. That column is a cache, and this is the check that keeps it honest |
| `Update_inserts_only_the_new_operations` | Load a payment with 2 operations, capture once, update: the table has 3 rows, not 5 |
| `A_failed_update_leaves_nothing_behind` | Force the second statement to fail and both are rolled back: no half-written payment |

### PagingTests (9) — *stretch A*, step 16

| Test | The rule it must check |
|---|---|
| `The_list_is_newest_first` | Three payments come back in reverse creation order |
| `The_list_only_shows_one_merchant` | A payment of merchant M2 never appears in M1's list |
| `Rows_with_the_same_timestamp_keep_one_order` | Five rows with an identical `created_at`, asked 20 times, come back in the same order all 20 times |
| `Walking_every_page_returns_each_row_once` | 25 rows, `limit=10`: follow the cursors to the end and every id appears exactly once |
| `Inserts_during_paging_do_not_lose_the_original_rows` | Insert new payments between page calls: all 25 original ids still appear exactly once |
| `The_limit_is_clamped_to_100` | `limit=5000` returns at most 100 |
| `A_zero_or_negative_limit_falls_back_to_the_default` | `limit=0` and `limit=-1` both return the default page size, not an empty page and not an error |
| `A_broken_cursor_is_400_invalid_cursor` | `cursor=nonsense`, a truncated base64 and a valid base64 of rubbish are each `400` with that `code` — never a `500`, never a silent page 1 |
| `The_last_page_has_a_null_next_cursor` | A page shorter than the limit reports `nextCursor: null` |

### BothStoresTests — *stretch B*, step 18

Not a new file. Turn every `[Fact]` in `StoreTests`, `CaptureTests` and `RefundTests` into a `[Theory]` with `[InlineData("dapper")]` and `[InlineData("ef")]`.

| Test | The rule it must check |
|---|---|
| *(the 12 existing tests, twice)* | Identical behaviour from both stores, with no `if` in a test body that names a store |
| `Both_stores_write_the_same_rows` | Run the same command through each store against a clean database and compare the resulting rows column by column. Identical, timestamps aside |

---

## Self-check before you call the list done

- [ ] Every test asserts something. A test with no `Assert` is a lie told in green.
- [ ] Every test arranges its own data. None depends on another test, or on test order.
- [ ] Every failure test also checks that **nothing changed** in the database.
- [ ] Every ProblemDetails test checks the `code`, not the `detail` text. `detail` is for humans and may be reworded.
- [ ] No test reaches into `Payments.Domain` to set up a state the API cannot reach. If the API cannot make it, it is not a state worth testing here.
- [ ] No real card numbers. Not even in a comment.
