# Lab 08 — Test list

Written by the agent: **the names and the rules**. Written by **you**: every body.

That split is the rule in `CLAUDE.md`. The name is yours to improve. The **rule** in the right column is not negotiable, and it is what the step's Proof checks.

How to use it:

```bash
grep -n '^###' labs/08-mcp-server/TEST-LIST.md                       # the groups
sed -n '/^### RefundConfirmTests/,/^### /p' labs/08-mcp-server/TEST-LIST.md
```

One group is one file. The first four groups and the last one live in `tests/Payments.Api.Tests` and talk to the API over `HttpClient`. The seven groups in between live in the new `tests/Payments.Mcp.Tests` and talk to the MCP server through the SDK's own client, over in-memory pipes, with a **stub API** behind it.

**Counts.** 67 tests in 12 groups: 21 on the API side, 42 on the MCP side, 4 end to end. A guide, not a target.

**Helpers send a key from the start (step 3).** From step 4 on, every `/payments` call needs an `X-Api-Key`. So in step 3, before the filter exists, `ApiTestBase` inserts one **service** key for `SeededMerchant` after every reset, with all three scopes, and puts it on `Client` as a default header. Until step 4 the server ignores it. After step 4 nothing old breaks. That is a change to a helper, required by the new contract — not a test edited to go green. Lab 03 did the same with `Idempotency-Key`.

**Two helpers the groups share.**

- API side: `HttpClient ClientWith(string[] scopes, string kind = "agent", Guid? merchant = null)` inserts a fresh key and returns a client that sends it.
- MCP side: `McpHarness` starts the server in-process and gives you an `McpClient` and a `StubApi`. `StubApi` records every request and answers from a queue you fill.

---

### ApiKeyTests (5) — step 3

| Test | The rule it must check |
|---|---|
| `No_key_is_401_api_key_required` | `[Theory]` with no header, `""` and `"   "`: each is `401`, code `api_key_required`, and the response has a `WWW-Authenticate` header |
| `An_unknown_key_is_401_api_key_invalid` | A key that was never inserted is `401 api_key_invalid` |
| `A_revoked_key_is_401_api_key_invalid` | Set `revoked_at`, call again: `401` with the **same** code as unknown. A caller cannot tell "revoked" from "never existed" |
| `Health_needs_no_key` | `GET /health` with no key is `200`. A load balancer has no key |
| `Only_the_hash_is_stored` | After inserting through the helper, `select count(*) from api_key where key_hash = @raw` is `0`, and `where key_hash = ApiKeyHash.Of(@raw)` is `1` |

### ScopeTests (5) — step 3

| Test | The rule it must check |
|---|---|
| `A_read_key_can_get_and_list` | A key with only `payments:read` gets `200` on `GET /payments/{id}` and on `GET /payments` |
| `A_read_key_cannot_refund` | `403`, code `insufficient_scope`, extension `required_scope` = `payments:refund` |
| `A_forbidden_refund_writes_nothing` | After that `403`, `payment_operation` has no `Refund` row and `refunded_amount` is still `0` |
| `A_refund_key_cannot_create_a_payment` | `payments:read` + `payments:refund` on `POST /payments` is `403`, `required_scope` = `payments:write` |
| `An_agent_key_with_approve_is_refused_by_the_database` | Inserting `kind = 'agent'` with `payments:approve` throws `PostgresException` with `SqlState` `23514` (check violation). The same scopes with `kind = 'service'` insert fine |

### MerchantBindingTests (3) — step 3

| Test | The rule it must check |
|---|---|
| `Another_merchants_payment_is_404` | A payment made for M1, read with an M2 key: `404 payment_not_found`. The same answer as an id that does not exist |
| `Another_merchants_payment_cannot_be_refunded` | Refund with an M2 key that has `payments:refund`: `404`, and no `Refund` row |
| `Create_for_another_merchant_is_403_merchant_mismatch` | An M1 service key posting `merchantId` = M2: `403 merchant_mismatch`, and no `payment` row |

### ListPaymentsTests (8) — step 5

Arrange rows with `ExecuteSqlAsync` when a test needs exact `created_at` values. Through the API they all land within the same millisecond.

| Test | The rule it must check |
|---|---|
| `An_empty_merchant_lists_nothing` | `items` is empty and `truncated` is `false`. `200`, never `404` |
| `Lists_only_the_keys_merchant` | Payments for M1 and M2 exist; an M1 key sees only M1's |
| `Newest_first_with_the_id_breaking_ties` | Two rows with the **same** `created_at`: the larger `id` comes first. Three calls, three identical orders |
| `At_most_20_by_default` | 25 payments, no `limit`: 20 items, `truncated` `true` |
| `Exactly_the_limit_is_not_truncated` | 20 payments, no `limit`: 20 items, `truncated` `false`. This is the test that catches a `count > limit` written as `count >= limit` |
| `The_limit_is_respected` | 7 payments, `limit=5`: 5 items, `truncated` `true` |
| `A_limit_outside_1_to_20_is_400` | `[Theory]` with `0`, `21`, `-1`: `400 limit_out_of_range` |
| `The_status_filter_is_exact` | Mixed statuses, `status=Captured`: only `Captured` rows. `status=captured` (lower case) is `400 invalid_status` |

### ToolListTests (5) — step 8

| Test | The rule it must check |
|---|---|
| `Exactly_three_tools_are_listed` | `tools/list` returns `get_payment`, `list_payments`, `refund_payment`, and nothing else |
| `Get_payment_requires_a_payment_id_string` | The schema has one property, `payment_id`, type `string`, and it is in `required`. Its `pattern` matches a UUID and rejects `42` and `order-1234`. SDK 2.3.0 emits no `format: uuid`, so the shape is carried by `[RegularExpression]` |
| `Refund_requires_id_amount_and_currency_but_not_the_token` | `required` is exactly `payment_id`, `amount`, `currency`. `amount` is type `string`, **not** `number`. `confirmation_token` is a property and is not required |
| `Read_tools_are_read_only_and_refund_is_destructive` | `readOnlyHint` is `true` on the two read tools. On `refund_payment`, `destructiveHint` is `true` and `readOnlyHint` is not `true` |
| `The_descriptions_match_the_snapshot` | Each description equals the text in the test, word for word. A changed sentence must change this test in the same commit |

### ArgumentFenceTests (7) — step 10

Every test here also asserts that `StubApi` recorded **zero** requests. A refused call never reaches the API.

| Test | The rule it must check |
|---|---|
| `An_unknown_argument_is_refused_by_name` | `get_payment` with an extra `merchant`: `isError`, code `unknown_argument`, and the message names `merchant` |
| `An_invented_cursor_is_refused` | `list_payments` with `cursor: "abc"`: `unknown_argument`. There is no paging yet, and the model must hear that, not get page 1 |
| `A_payment_id_that_is_not_a_uuid_is_refused` | `"42"` and `"order-1234"`: code `invalid_payment_id`, and the message says a UUID is expected. Not the SDK's "An error occurred invoking" |
| `Malformed_money_is_refused` | `[Theory]`: amount `"80,00"`, `"80,00 euro?"`, `"1e3"`, `"-5"`, `" 80.00"`, `"80.005"`, `""` → `invalid_amount`; currency `"eur"`, `"EURO"`, `"€"` → `invalid_currency` |
| `A_zero_amount_is_refused` | `"0"` and `"0.00"` match the pattern and are still `invalid_amount`: not positive |
| `A_status_outside_the_enum_is_refused` | `"captured"` and `"Settled"`: `invalid_status`. The schema lists the enum; the server must still check it |
| `A_limit_outside_1_to_20_is_refused` | `0` and `500`: `invalid_limit`. Same reason |

### ApiKeyHeaderTests (3) — step 12

| Test | The rule it must check |
|---|---|
| `Every_api_call_carries_the_configured_key` | After one call of each tool, every request `StubApi` saw has `X-Api-Key` equal to the configured value |
| `The_server_refuses_to_start_without_a_key` | Building the harness with no `PAYMENTS_API_KEY` throws at start, with a message that names the variable |
| `The_key_never_reaches_the_model` | `StubApi` answers `500` with the key echoed in the body: the tool result text does not contain the key, and says `tool_failed` |

### ReadToolTests (6) — step 12

| Test | The rule it must check |
|---|---|
| `Get_payment_returns_the_payment` | Stub `200` with a DTO: `result` is `found`, `payment.id` matches, `isError` is not `true` |
| `A_missing_payment_is_a_result_not_an_error` | Stub `404`: `isError` is not `true`, `result` is `not_found`, `payment_id` echoed |
| `A_403_is_insufficient_scope_with_a_hint` | Stub `403 insufficient_scope`: `isError`, code `insufficient_scope`, and a hint that a human must do it |
| `List_passes_status_and_limit_through` | `status: "Captured", limit: 5`: the stub saw `GET /payments?status=Captured&limit=5` |
| `List_defaults_to_20` | No arguments: the stub saw `limit=20` |
| `List_passes_truncated_through_with_a_hint` | Stub `truncated: true`: the result has `truncated: true` and a hint to narrow with `status` |

### ConfirmationTokenTests (7) — step 14

Unit tests on the token store alone. `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` is the clock.

| Test | The rule it must check |
|---|---|
| `A_proposal_gets_an_unguessable_token` | Starts with `rf_`, at least 22 characters after it, and 1 000 proposals give 1 000 different tokens |
| `The_same_arguments_redeem` | Propose `(id, 80.00m, "EUR")`, redeem with the same: the claim is won |
| `Other_arguments_are_a_mismatch_and_keep_the_token` | Redeem with `79.99m`: `Mismatch`. Then with `80.00m`: still won. A mismatch does not burn the token |
| `A_token_lives_five_minutes` | Advance 4 min 59 s: won. A fresh token, advance 5 min: `Expired` |
| `An_unknown_token_is_unknown` | `rf_doesnotexist`: `Unknown` |
| `A_done_token_returns_its_stored_outcome` | Claim, complete with an outcome, redeem again: `AlreadyDone` with that exact outcome |
| `Two_claims_at_once_one_wins` | 20 parallel claims on one token: exactly one won, the rest `InProgress` |

### RefundProposeTests (5) — step 16

| Test | The rule it must check |
|---|---|
| `A_proposal_posts_nothing` | No token in the call: the stub saw exactly one `GET` and **zero** `POST` |
| `The_proposal_names_the_amount_currency_and_payment` | `result` is `confirmation_required`; `proposal` contains `80.00`, `EUR` and the payment id; `confirmation_token` and `expires_at` are present; `next` says to ask the human first |
| `More_than_refundable_is_refused_without_a_token` | Stub payment with 30.00 refundable, ask 80.00: `amount_exceeds_captured`, the message says `30.00`, and no token is in the result |
| `Another_currency_is_refused_without_a_token` | Stub payment in EUR, ask USD: `currency_mismatch`, no token |
| `A_missing_payment_is_refused_without_a_token` | Stub `404`: `isError`, `payment_not_found`. For a refund that is an error, not a fact |

### RefundConfirmTests (9) — step 16

| Test | The rule it must check |
|---|---|
| `A_confirm_posts_exactly_one_refund` | Propose, confirm: the stub saw one `POST /payments/{id}/refund` with `{"amount":80.00,"currency":"EUR"}`, and the result is `refunded` |
| `The_refund_carries_an_idempotency_key_from_the_token` | That `POST` has `Idempotency-Key` = `mcp-` + the token |
| `A_repeated_confirm_replays_and_posts_nothing` | Confirm twice: still one `POST`, and the second result is the first one plus `replayed: true` |
| `A_changed_amount_is_token_mismatch` | Propose 80.00, confirm with 79.99: `token_mismatch`, zero `POST` |
| `An_expired_token_posts_nothing` | Advance the clock 5 minutes, confirm: `token_expired`, zero `POST` |
| `An_invented_token_is_token_unknown` | `rf_madeup`: `token_unknown`, zero `POST` |
| `A_403_on_confirm_is_insufficient_scope` | Stub answers the `POST` with `403`: `insufficient_scope`. A repeat confirm replays it and posts nothing |
| `A_lost_answer_is_in_doubt_and_the_retry_sends_the_same_key` | The stub throws `HttpRequestException` on the first `POST`, answers `200` on the second: the first result is `refund_in_doubt`, the second is `refunded`, and both `POST`s carry the **same** `Idempotency-Key` |
| `Two_confirms_at_once_post_once` | The stub holds the first `POST` open; a second confirm meanwhile gets `confirmation_in_progress`. Release it: one `POST` in total |

### McpEndToEndTests (4) — steps 18 and 19

The MCP server in-process, its `HttpClient` wired to `Fixture.Factory.Server.CreateHandler()`, so it calls the **real** API on the real throwaway Postgres.

| Test | The rule it must check |
|---|---|
| `The_double_charge_story_refunds_once` | Two captured 80.00 EUR payments. `list_payments(status: Captured)` shows both; propose on one; confirm; confirm again. Exactly **one** `Refund` row in `payment_operation`, on the right payment |
| `A_read_only_agent_cannot_refund` | An agent key with `payments:read` only: the proposal works, the confirm is `insufficient_scope`, and there is no `Refund` row |
| `An_agent_sees_only_its_own_merchant` | An M2 agent key: `get_payment` on an M1 payment is `not_found`; `list_payments` has none of M1's |
| `A_dropped_answer_still_refunds_once` | **Step 19, after lab 03 is merged.** A `DelegatingHandler` lets the first refund reach the API, then throws instead of returning. The tool says `refund_in_doubt`; the same confirm again says `refunded`. Exactly **one** `Refund` row |
