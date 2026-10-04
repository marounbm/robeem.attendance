# Attendance P3 — normalized layer: notes

Prompt 3 added `NormalizedAttendanceTransaction`, its duplicate-key index, `INormalizedTransactionRecorder`, and the `AddNormalizedAttendanceTransactions` migration. This file records where the result differs from, or fills a gap in, the prompt's spec.

## Deviations

| Item | Spec | What was done | Why |
|---|---|---|---|
| Duplicate-key unique index | `HasIndex(TenantId, EmployeeId, DeviceId, NormalizedEventType, DuplicateBucketTimestampUtc).IsUnique()` | Same columns, but **filtered**: `WHERE "DuplicateStatus" = 'Unique'`. | A DuplicateConfirmed row has, by definition, the same key as the row it duplicates. A plain unique index would reject it, so "one Unique + one DuplicateConfirmed" could never be stored. The filter keeps at most one canonical row per key. That is all the race backstop needs. |
| `EventTimestampLocal` | `DateTimeOffset` | `DateTime`, stored as `timestamp without time zone`. It holds the wall-clock time in `TimeZoneId`. | PostgreSQL `timestamp with time zone` can't store an offset. Npgsql refuses a non-zero one, and P2's converter turns every `DateTimeOffset` into UTC. As a `DateTimeOffset`, the column would always come back equal to `EventTimestampUtc`. The offset can be derived from `TimeZoneId` and `EventTimestampUtc`. Recorded in CLAUDE.md §2.8. |
| Where the catch-and-convert lives | "catch DbUpdateException … convert into DuplicateConfirmed" | A new service, `INormalizedTransactionRecorder.RecordAsync`, in `src/robeem.Application/Attendance`, registered as scoped. It checks the key, sets `DuplicateStatus`/`DuplicateOfTransactionId`, and saves. If a concurrent writer wins the race, it catches the `DbUpdateException` (the `EmployeeService` pattern), looks up the winning row, and saves the row again as DuplicateConfirmed. | The prompt asks for tests that two rows yield Unique + DuplicateConfirmed, so the logic had to exist now. **P5 should add every normalized row through this recorder** rather than reimplement it. |
| Rows without an employee | — | Always stored as `Unique`. There is no duplicate check. | Punches from different unrecognized employees would share a key: null employee, same device, type and bucket. Marking them duplicates would hide real punches. P5 flags them `FlaggedUnrecognizedEmployee` anyway. |
| Rows without a device | — | Checked, with null equal to null (for example two web-portal check-ins in one bucket). | — |
| Defaults | — | `NormalizedEventType`: `Unknown`. `EventSource`: `Device`. `ConfidenceStatus`: `Certain`. `ProcessingStatus`: `Pending`. `DuplicateStatus`: `Unique`. `ExceptionStatus`: `None`. | `Unknown` avoids silently defaulting to `CheckIn`. P5 should set `ConfidenceStatus` explicitly (invariant 6). |
| `OriginalEventType` | nullable string | Unbounded `text`. | It is copied from vendor data; see the P2 notes. |
| `RawTransactionId` | required FK | Indexed, **not unique**. | The spec doesn't say that one raw row yields at most one normalized row. |

## Limits of the backstop

- **Rows without a device.** In the unique index, NULLs are distinct. So a race between two rows whose `DeviceId` is null is not caught by the index; only the recorder's pre-check catches duplicates for such rows. PostgreSQL 15+ could close this with `NULLS NOT DISTINCT` (`AreNullsDistinct(false)`), but SQLite has no equivalent, so it was not added.
- **The recorder saves the whole context.** The caller's other pending changes, such as the raw row's `ProcessingStatus`, commit with the normalized row.
- **No outer transaction.** It must not run inside an explicit transaction. On PostgreSQL the failed insert would abort that transaction, so the retry could not run.

## Test support change

`TestDatabase.CreateContext` takes optional EF interceptors. The race test uses one to insert a competing row with the same key after the recorder's pre-check, so the unique index and the catch path are really exercised.

## Verification

- **Build:** `dotnet build` finishes with 0 warnings and 0 errors.
- **Format:** `dotnet format --verify-no-changes` passes.
- **Tests:** all 64 pass. The new file is `tests/robeem.Tests/Infrastructure/Attendance/NormalizedAttendanceTransactionTests.cs`, with five tests:
  - a new key is Unique;
  - the same key twice gives Unique + DuplicateConfirmed, with `DuplicateOfTransactionId` pointing at the first row;
  - a different event type is Unique;
  - a lost race becomes DuplicateConfirmed;
  - rows without an employee are never duplicates.
- **Migration on PostgreSQL:** running the Api in Development applied `AddNormalizedAttendanceTransactions` to both tenant databases.
