# Lab 04 — Test list

Written by the agent: **the names and the rules**. Written by **you**: every body.

That split is the rule in `CLAUDE.md`. The name is yours to improve. The **rule** in the right column is not negotiable, and it is what the step's Proof checks.

How to use it:

```bash
grep -n '^###' labs/04-sqlserver-procs-locking/TEST-LIST.md                    # the groups
sed -n '/^### DeadlockTests/,/^### /p' labs/04-sqlserver-procs-locking/TEST-LIST.md
```

One group is one file in `tests/Payments.Data.SqlServer.Tests`. Every class is `[Trait("runs", "local")]` and takes `IClassFixture<SqlServerDatabase>`, so every class gets its own fresh database.

**Counts.** 42 tests in 8 groups. A guide, not a target.

**Helpers the tests share (step 7).** In `SqlServerTestBase`, not in the tests:

- `SeedPaymentAsync(status, authorised = 100m, captured = 0m, currency = "EUR", merchant = M1)` inserts one `Payment` row straight with Dapper and returns its id. A test arranges the exact state it needs; it never walks the state machine to get there.
- `ReadPaymentAsync(id)` reads the row back, `RowVer` included.
- `CountOperationsAsync(id)` counts its `PaymentOperation` rows.
- `Procs` is a `SqlServerPaymentProcs` on this class's database.

**Race tests.** Build every call **before** you fire them, then `Task.WhenAll`. Building them inside the loop serialises the start, and the test stops testing anything while still passing (learned in lab 03). Judge a race test over 10 runs, not one.

---

### SchemaTests (5) — step 7

| Test | The rule it must check |
|---|---|
| `The_database_has_RCSI_on` | `is_read_committed_snapshot_on = 1` in `sys.databases` for this class's database |
| `Payment_has_a_RowVer_column_of_type_rowversion` | `sys.columns` joined to `sys.types` says `timestamp` (SQL Server's old name for `rowversion`) |
| `The_two_merchants_are_seeded` | `M1 Hotel Sofia` and `M2 Retail Plovdiv`, same ids as Postgres — the lockstep rule |
| `Migrating_twice_runs_nothing_the_second_time` | A second `Migrator.Run` on the same database is a success with **0** scripts run |
| `A_missing_script_folder_is_a_failed_Result_not_an_exception` | `Migrator.Run` with a folder that does not exist returns `IsSuccess == false` and an error that names the folder. No throw (`CF-009`) |

### CaptureProcTests (11) — steps 9–10

| Test | The rule it must check |
|---|---|
| `Capturing_the_full_amount_makes_it_Captured` | 100 of 100: `Status = 'Captured'`, `CapturedAmount = 100` |
| `Capturing_part_makes_it_PartiallyCaptured` | 40 of 100: `PartiallyCaptured`, `CapturedAmount = 40` |
| `Capturing_exactly_what_is_left_is_allowed` | 60 after 40 of 100 succeeds and ends `Captured`. The limit is inclusive |
| `Capturing_more_than_authorised_is_amount_exceeds_authorised` | Failed `Result`, code `amount_exceeds_authorised`; the row is unchanged |
| `Capturing_a_Created_payment_is_illegal_transition` | State is checked first: on `Created` it is `illegal_transition`, even with a bad amount too |
| `Capturing_in_another_currency_is_currency_mismatch` | `USD` on a `EUR` payment |
| `Capturing_zero_is_amount_not_positive` | `0` is refused with that code |
| `Capturing_an_unknown_payment_is_payment_not_found` | A fresh `Guid` |
| `A_capture_writes_exactly_one_operation_row` | One `PaymentOperation` row, `Kind = 'Capture'`, the amount, the `@OperationId` and `@At` you passed |
| `A_refused_capture_writes_nothing` | After any refusal: `RowVer`, `CapturedAmount` and the operation count are all unchanged |
| `A_capture_changes_RowVer` | `RowVer` after is not equal to `RowVer` before. Compare the bytes, not the reference |

### CaptureRaceTests (3) — step 11

| Test | The rule it must check |
|---|---|
| `Fifty_parallel_full_captures_capture_once` | 50 × capture 100 of 100: exactly **1** success, 49 `illegal_transition`, `CapturedAmount = 100`, **1** operation row |
| `Fifty_parallel_captures_of_ten_never_exceed_the_authorised_amount` | 50 × capture 10 of 100: exactly **10** successes and 40 `illegal_transition`, `CapturedAmount = 100`, **10** operation rows. Why not `amount_exceeds_authorised`? Every 10 fits until the payment is full, and full means `Captured` — and state is checked first. If you see that code, your `@@ROWCOUNT = 0` branch checks in the wrong order |
| `No_racer_ever_gets_a_raw_SqlException` | Across both storms, every call returned a `Result`. Not one `SqlException` escaped — no 1205, no 2627 |

### IsolationTests (3) — step 12

| Test | The rule it must check |
|---|---|
| `A_reader_is_not_blocked_by_an_open_capture` | Connection A opens a transaction and runs the capture `UPDATE` without committing. Connection B reads the row with a 2 s command timeout: it returns at once with the **old** status. That is RCSI |
| `A_reader_sees_the_capture_once_it_commits` | Same setup; A commits; B reads again and sees `Captured` |
| `A_locking_read_waits_for_the_writer` | Same setup, but B reads `WITH (READCOMMITTEDLOCK)` and a 1 s command timeout: B times out (`SqlException`, number `-2`). RCSI is a database default, not a law; a hint brings the old blocking back |

### DuplicatePairTests (7) — steps 13–14

| Test | The rule it must check |
|---|---|
| `Marking_a_pair_sets_DuplicateOf_on_the_duplicate_only` | `Duplicate.DuplicateOf = KeepId`; `Keep.DuplicateOf` stays `NULL` |
| `Marking_a_pair_twice_is_pair_already_marked` | Same call again: `pair_already_marked`, nothing changes |
| `Marking_it_the_other_way_round_is_pair_already_marked` | After (A keep, B dup), the call (B keep, A dup) is refused. One pair, one mark |
| `A_payment_paired_with_itself_is_pair_not_eligible` | `@KeepId = @DuplicateId` |
| `A_pair_across_two_merchants_is_pair_not_eligible` | One on M1, one on M2 |
| `A_pair_that_is_not_both_Captured_is_pair_not_eligible` | One `Captured`, one `Authorised` |
| `A_pair_with_an_unknown_id_is_payment_not_found` | Either side unknown |

### DeadlockTests (3) — steps 13–16

All three run with `@PauseMs = 500`, so both callers hold their first lock before either asks for the second. **Red in step 14 with 1205. Green in step 16.**

| Test | The rule it must check |
|---|---|
| `Flagging_from_both_sides_at_once_lets_exactly_one_win` | (A keep, B dup) and (B keep, A dup) start together: exactly **one** success and one `pair_already_marked`. No `SqlException` |
| `The_loser_waits_and_then_sees_the_winners_mark` | The loser's answer is `pair_already_marked`, and it took at least `@PauseMs`: it **waited** for the lock, it was not killed. Exactly one of the two rows has a `DuplicateOf` |
| `Twenty_rounds_from_both_sides_never_deadlock` | 20 fresh pairs, each flagged from both sides at once: 20 wins, 20 `pair_already_marked`, 0 exceptions |

### ClaimTests (6) — steps 17–18

| Test | The rule it must check |
|---|---|
| `A_claim_takes_the_oldest_ready_jobs_first` | 10 `Ready` jobs, claim 3: the 3 lowest `Id`s come back |
| `A_claim_marks_who_and_when` | Every claimed row has `Status = 'Claimed'`, `ClaimedBy = @WorkerId`, `ClaimedAt = @At` |
| `A_claim_never_takes_more_than_the_batch_size` | 10 ready, batch 4: exactly 4 |
| `An_empty_queue_returns_nothing_at_once` | 0 ready: an empty list, no exception, no wait |
| `A_second_worker_skips_rows_held_by_the_first` | Worker 1 claims 5 inside an **open** transaction. Worker 2 claims 5 with a 1 s command timeout: it returns at once with **5 other** ids. That is `READPAST` |
| `Five_workers_claim_two_hundred_jobs_exactly_once` | 200 ready; 5 workers loop `claim 7` until empty, all at once. The union is all 200 ids, no id twice, every row `Claimed` |

### EfCoreTests (4) — steps 19–20

| Test | The rule it must check |
|---|---|
| `A_second_save_of_a_stale_row_throws_DbUpdateConcurrencyException` | Two contexts load the same payment. The first changes `UpdatedAt` and saves. The second changes it and saves: `DbUpdateConcurrencyException`. The row holds the first save |
| `Reloading_after_the_conflict_lets_the_save_through` | Catch it, `entry.ReloadAsync()`, change again, save: success, and `RowVer` moved again |
| `The_capture_proc_runs_from_EF_with_ExecuteSqlAsync` | `ExecuteSqlAsync($"EXEC dbo.usp_CapturePayment {id}, …")` captures; a fresh context reads `Captured` |
| `A_refusal_from_the_proc_reaches_EF_as_SqlException_50005` | An over-capture through EF throws a `SqlException` whose `Number` is `50005`. EF does not map it for you; that is your code's job |
