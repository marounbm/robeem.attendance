# Attendance P5 — normalization engine: notes

## What Prompt 5 added

| Piece | Where | What it is |
|---|---|---|
| `INormalizationService` / `NormalizationService` | Application | Two methods: `NormalizeBatchAsync` and `ListBatchesWithPendingTransactionsAsync`. The second is what the hosted service polls. |
| `ExternalEmployeeIdMapping` | Domain | The entity named in the spec. Migration `AddExternalEmployeeIdMapping`. |
| `IAttendanceEventInterpreter` | Application | A per-vendor mapping seam, keyed by `ProviderKey`. |
| `GenericCsvInterpreter` | Infrastructure | The CSV vendor's interpreter. |
| `IAttendancePolicyResolver` | Application | Shared with P7. |
| `AttendanceNormalizationHostedService` | `src/robeem.Api/Attendance` | The background poller. Configured in section `Attendance:Normalization`: `Enabled`, default true; `PollIntervalSeconds`, default 10. |

This file records where the result differs from, or fills a gap in, the prompt's spec.

## Choices to review

### 1. Unlabeled punches depend on the policy (invariants 6 and 5)

Step 1e says a raw row with "no usable type/direction at all" fails. Invariant 6 says a tenant may opt in to interpreting unlabeled punches. Both are honored:

| Case | Result |
|---|---|
| Type and direction both blank, policy mode `Off` (the default) | The raw row fails, as in step 1e. |
| Type and direction both blank, mode `Sequential` or `RulesEngine` | A normalized row is made with type `Unknown` and confidence `Ambiguous`. Processing (P7) assigns the type and marks it `Inferred`. Nothing is invented: the punch exists, only its label is missing. |
| A type or direction the vendor's interpreter **doesn't recognize** | The raw row fails, whatever the mode, with "Unrecognized event type 'X' for provider 'Y'". The fix is to extend the interpreter, then set the row back to Pending. |

### 2. Per-vendor interpreter (step 1c)

`IAttendanceEventInterpreter` has two methods: `ParseTimestamp` and `MapEventType`. It is registered as a keyed service under the same key as the vendor's `IAttendanceProvider`; CLAUDE.md §4 now says so.

Timestamp parsing moved into the interpreter as well. Reason: when ingestion couldn't compute an instant (a wall-clock time from a device not yet registered), normalization must re-parse the vendor's text, and the timestamp format is vendor knowledge. `GenericCsvProvider` uses the interpreter, so P4 behavior is unchanged.

`GenericCsvInterpreter` matches values ignoring case and punctuation, trying the type first and then the direction:

| Value | Event type |
|---|---|
| `IN`, `CHECKIN`, `CLOCKIN` | CheckIn |
| `OUT`, `CHECKOUT`, `CLOCKOUT` | CheckOut |
| `BREAKOUT`, `BREAKSTART` | BreakOut |
| `BREAKIN`, `BREAKEND` | BreakIn |

### 3. Which instant a punch is at

The UTC instant is the first of these that is available:

1. `TransactionTimestampParsed`, set at ingestion;
2. the vendor's offset;
3. the time zone of the resolved device.

If none applies (a wall-clock time from an **unknown** device), the raw row **fails**, with a message asking to register the device and reprocess. Step 1d says unresolved devices still produce a row. They do, whenever the instant is known, for example when the vendor sends an offset. But a normalized row needs a real instant, and guessing a time zone would invent data.

### 4. Local time and date for an unknown device

The device's time zone is unknown, so the row gets:

- `TimeZoneId` = `UTC`;
- `EventTimestampLocal` = the UTC wall-clock time;
- `AttendanceDate` = the UTC date.

The row is flagged, and needs a person anyway. With a known device, the device's IANA zone is used.

### 5. `AttendanceDate`

It is the local calendar date. Assigning an overnight shift's 06:00 check-out to the previous day's shift is schedule-aware work, so it belongs to P7.

### 6. When both the employee and the device are unknown

`ExceptionStatus` holds one value, so the employee flag wins: `FlaggedUnrecognizedEmployee`.

### 7. Duplicate detection (step 1f) reuses P3's recorder

Each normalized row goes through `INormalizedTransactionRecorder`. It takes the duplicate decision and handles the concurrent-race fallback. Its single save also commits the raw row's `ProcessingStatus = Normalized` (step 1g), so the raw and normalized rows can't get out of step.

Each raw row is saved on its own, so an interrupted run resumes where it stopped. A large batch therefore costs one round trip per row; revisit if uploads get big.

### 8. Policy resolution

`AttendancePolicyResolver` takes the first of these that exists:

1. the policy of the employee's schedule;
2. the tenant-wide default;
3. an unsaved built-in default (120 s window, mode `Off`).

An unknown employee gets the tenant default. Results are cached per run.

### 9. Fixed time buckets have edge cases

The bucket is `EventTimestampUtc` floored to the window, the fixed bucketing of study §10.3. Two punches two seconds apart can straddle a bucket boundary, for example 06:01:59 and 06:02:01 with 120 s, so they are not detected as duplicates. P8's sequence checks will see such a pair as two check-ins.

### 10. Unexpected errors

An unexpected error on one row:

- is logged;
- marks that raw row Failed, with `Normalization error: <root cause>`;
- does not stop the rest of the batch.

A row can be retried by setting it back to Pending; `ProcessingStatus` is one of the raw row's mutable columns. Cancellation propagates.

A missing interpreter for a provider throws instead, and the rows stay Pending. That is a configuration problem, not a data problem. The hosted service logs it each poll until it is fixed.

### 11. Hosted service

- Every `PollIntervalSeconds`, it goes through `ITenantRegistry.Tenants`. For each tenant it creates `ITenantScopeFactory.CreateScope(tenant.Id)`, lists the batches with Pending rows, and normalizes them.
- A failure for one tenant is logged and doesn't stop the others.
- It starts after the startup migration, because `InitializeAllAsync` completes before `RunAsync`.
- `IngestionService` does not call normalization; the hosted service is the only caller.
- **Run one Api instance only.** Two instances would normalize the same raw row twice. The second row would have the same duplicate key, so it would be stored as DuplicateConfirmed rather than double-counted. Even so, P10's scheduler should add a lock or claim mechanism.

## Bug found by the PostgreSQL run, and the guard that now catches it

The first run against `robeem_demo` normalized four rows. It failed T3, the unknown-device punch with an offset. Cause: with the UTC fallback, `AttendanceDate` was `utc.UtcDateTime.Date`, which has `DateTimeKind.Utc`. Npgsql refuses that kind for `timestamp without time zone`, but SQLite accepts it, so the tests passed.

Fixes:

- **The service** now gives the local value `Unspecified` kind.
- **New test guard:** `tests/robeem.Tests/UtcDateTimeGuardInterceptor.cs`. `TestDatabase` adds it to every context, and it makes SQLite reject UTC-kind `DateTime` writes the way PostgreSQL does. With the service fix reverted, it fails exactly the unknown-device test, so it would have caught this bug.
- **P3's test helper** had the same mistake in its test data. The guard flagged it, and it is fixed.
- **CLAUDE.md §2.8** now mentions the guard.

After the fix, T3 was set back to Pending, which also exercised the reprocess path. The next poll normalized it correctly.

## Verification

- **Build:** `dotnet build` finishes with 0 warnings and 0 errors.
- **Format:** `dotnet format --verify-no-changes` passes.
- **Tests:** all 77 pass. The new file is `tests/robeem.Tests/Attendance/NormalizationServiceTests.cs`, with 9 tests.
  - The five scenarios the prompt asked for:
    - a resolvable punch is normalized, with the Beirut wall-clock time, the UTC instant and the bucket;
    - an unresolvable employee gives a flagged row;
    - an unresolvable device gives a flagged row in UTC;
    - an invalid timestamp gives no normalized row, and the raw row fails;
    - a duplicate pair gives Unique + DuplicateConfirmed, the second pointing at the first.
  - Four more:
    - the schedule policy's duplicate window is used instead of the default's;
    - an unlabeled punch fails under `Off` and is kept as Unknown/Ambiguous under `Sequential`;
    - an unrecognized type fails the raw row;
    - the pending-batch listing works.
- **End to end on PostgreSQL:** the Api's hosted service normalized P4's smoke-test batch in `robeem_demo`:
  - T1, T2 and T5 are normalized, flagged `FlaggedUnrecognizedEmployee` because no mapping exists, with correct Beirut local times;
  - T3 is normalized, with an offset timestamp from an unknown device, so its time zone is UTC;
  - T4 stays Failed from ingestion.

  More smoke-test rows now sit in `robeem_demo`: 4 normalized rows.
