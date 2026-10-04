# Attendance P2 — raw storage: notes

Prompt 2 added `AttendanceProcessingBatch`, `RawAttendanceTransaction`, the immutability guard in `RobeemDbContext`, `IRawPayloadSanitizer`, and the `AddAttendanceRawStorage` migration. This file records where the result differs from, or fills a gap in, the prompt's spec.

## The immutability guard is per column, not per row — needs a decision

The prompt asks for an `InvalidOperationException` on **any** `Modified` or `Deleted` raw row. Prompt 5 contradicts that: normalization sets the raw row's `ProcessingStatus` to `Normalized` or `Failed` and fills `ProcessingError` (steps 1e and 1g). Retention will likewise need to move `RetentionStatus`. A per-row guard would make all three columns impossible to change after insert.

What was implemented:

- **Delete** always throws `InvalidOperationException("RawAttendanceTransaction is immutable.")`.
- **Update** throws the same message, plus the names of the offending columns, if any column other than `ProcessingStatus`, `ProcessingError` or `RetentionStatus` is modified. The allowed list is `RawAttendanceTransaction.MutableProperties`.
- Everything the vendor sent, and when and how robeem received it, cannot change.

**Decision needed:** keep this, or move the three bookkeeping columns to a separate table and make the raw row fully immutable. CLAUDE.md §1.2 still reads "no code path may UPDATE a raw transaction row". If this design stays, that sentence should say "no vendor-captured column".

## Known gap: bulk operations bypass the guard

`ExecuteUpdate` and `ExecuteDelete` bypass the change tracker, so they also bypass `SaveChanges` and the guard. Nothing uses them today. Closing the gap for good would take a PostgreSQL trigger that rejects `DELETE` and any `UPDATE` touching other columns. That was not added, because it was not asked for.

## Other choices

| Item | Spec | What was done | Why |
|---|---|---|---|
| Vendor-supplied strings (`DeviceExternalId`, `ExternalTransactionId`, `ExternalEmployeeId`, `TransactionTimestampRaw`, `TransactionTypeRaw`, `DirectionRaw`, `ApiRequestReference`) | type only | No max length (`text`). | Invariant 1 says raw rows are always stored. A length limit would make an oversized vendor value fail the insert. |
| `RawPayload` | unbounded string | Unbounded, required. Not `jsonb`. | As specified. |
| `SourceSystem`, `RecordHash` | type only | Required. Max 255 and 128. | Both are set by robeem, not the vendor. 128 fits a SHA-512 hex digest. |
| `RecordHash` index | indexed | `(TenantId, RecordHash)`, **not unique**. | Raw storage is duplicate-tolerant (§1.4). |
| Extra index | — | `(TenantId, ImportBatchId, ProcessingStatus)`. | This is P5's query: the pending rows of a batch. |
| `DateTimeOffset` columns | `DateTimeOffset` | Converted to UTC on write, for every `DateTimeOffset` property on every provider. Recorded in CLAUDE.md §2.8. | Npgsql refuses a non-zero offset in `timestamp with time zone`. Without the converter, a punch parsed as `+03:00` would fail the insert. The original offset survives in `TransactionTimestampRaw`. |
| Batch `StartedAt` / `CompletedAt` | `DateTime` | Local wall-clock time (`DateTime.Now` default), like the other `DateTime` columns. | Follows §2.8. |
| Defaults | — | Batch `Status`: `Running`. Raw `ProcessingStatus`: `Pending`. `RetentionStatus`: `Active`. `RecordReceivedAt`: `DateTimeOffset.UtcNow`. | — |
| Sanitizer input | `string` or dictionary | `string Sanitize(string rawJson)`. It removes properties at any depth, including inside arrays. | — |
| Sanitizer matching | name contains a term | Case-insensitive, and non-alphanumeric characters are ignored, so `face_print` matches `faceprint`. | Catches common naming variants. |
| Sanitizer output | — | A payload with nothing to remove comes back as the **same string**. Otherwise it is re-serialized, keeping non-ASCII text unescaped. | Keeps payloads vendor-exact whenever possible. |
| Invalid JSON | — | Throws `FormatException`. | A payload that can't be parsed can't be checked, so it isn't stored silently. The adapter (P4) decides what to store in that case. |
| Configurable deny-list | configurable | `RawPayloadSanitizerOptions.AdditionalDenyTerms`, bound from `Attendance:RawPayloadSanitizer` in the Api. The four default terms always apply. | Configuration can add terms but never disable the defaults. |
| Registration | like `IEmployeeService` (scoped) | Singleton. | It is pure and stateless. |

The sanitizer is a backstop. Each adapter must still allowlist the fields it copies (invariant 10), because a deny-list can't catch a template stored under a harmless name.

## Verification

- **Build:** `dotnet build` finishes with 0 warnings and 0 errors.
- **Format:** `dotnet format --verify-no-changes` passes.
- **Tests:** all 59 pass. New ones:
  - `tests/robeem.Tests/Infrastructure/Attendance/RawAttendanceTransactionTests.cs` (5 tests): insert with UTC conversion, update of a vendor field throws, update of a detached row throws, delete throws, bookkeeping update is allowed.
  - `tests/robeem.Tests/Attendance/RawPayloadSanitizerTests.cs` (4 tests).
- **Migration on PostgreSQL:** running the Api in Development applied `AddAttendanceRawStorage` to both tenant databases.
- **Not tested:** the UTC conversion against PostgreSQL itself. It is tested only on SQLite, because no endpoint writes raw rows yet.
