# Attendance P4 — vendor seam and manual-upload ingestion: notes

Prompt 4 added:

- **`IAttendanceProvider`** and its models (`src/robeem.Application/Attendance`);
- **`GenericCsvProvider`** (`src/robeem.Infrastructure/Attendance/Providers`);
- **`IIngestionService`**;
- **`AttendanceIngestionController`**, endpoint `POST api/attendance/ingestion/{integrationId}/uploads` (multipart field `file`, 10 MB limit);
- **`AttendanceIngestionResult`** (`robeem.Contracts.Attendance`).

This file records where the result differs from, or fills a gap in, the prompt's spec.

## Choices to review

### 1. Interface signatures

The study isn't in the repository, so the §5.3 signatures were not available. I chose these:

```csharp
Task<ConnectionValidationResult> ValidateConnectionAsync(AttendanceIntegrationConfig, CancellationToken);
Task<AuthResult> AuthenticateAsync(AttendanceIntegrationConfig, CancellationToken);
Task<IReadOnlyList<ExternalDeviceDto>> RetrieveDevicesAsync(AttendanceIntegrationConfig, CancellationToken);
Task<IReadOnlyList<ExternalEmployeeDto>> RetrieveEmployeesAsync(AttendanceIntegrationConfig, CancellationToken);
Task<AttendanceBatchResult> RetrieveAttendanceTransactionsAsync(AttendanceIntegrationConfig, DateRange?, CancellationToken);
Task<AttendanceBatchResult> GetTransactionChangesAsync(AttendanceIntegrationConfig, SyncCursor?, CancellationToken);
```

**Please compare them with the study §5.3.**

### 2. Extra models

The prompt listed eight models. I added two:

- **`ExternalAttendanceTransaction`**: one row inside `AttendanceBatchResult`. Its fields are the payload, the raw timestamp, and the identifiers the adapter picked out.
- **`VendorTimestamp`**: a parsed wall-clock time plus an optional offset.

`AttendanceIntegrationConfig` carries `UploadFilePath` for manual uploads. All the models are records with no reference to `robeem.DataModel` or EF Core.

### 3. Timestamp parsing is split between the adapter and ingestion

The prompt says the adapter must not parse or interpret the row. But `RawAttendanceTransaction` is immutable after insert (P2), so `TransactionTimestampParsed` can only be set at ingestion. If it isn't set then, it stays null forever. So the parsing is split:

- **The adapter parses the format only.** A timestamp format is vendor knowledge, and invariant 9 keeps vendor knowledge in adapters. `GenericCsvProvider` accepts ISO 8601 only:
  - `yyyy-MM-dd HH:mm[:ss[.f…]]`, with an optional `T` separator;
  - optionally `Z` or an offset such as `+03:00`.

  It deliberately doesn't guess other formats: `04/10/2026` could be April or October.
- **Ingestion turns it into a UTC instant.** It uses the vendor's offset when there is one. Otherwise it uses the time zone of the device, resolved by `ExternalDeviceId` within the integration. With neither, `TransactionTimestampParsed` stays null, the row stays Pending, and P5 works out the instant.
- **Event type and direction are not interpreted.** They are copied as text for P5.

### 4. What `RecordsFailed` means

A row whose timestamp doesn't parse is still stored (invariant 1). It is inserted with `ProcessingStatus = Failed` and a `ProcessingError`, and counted in `RecordsFailed`.

`RecordsIngested` counts **all** stored rows, failed ones included. So the sample gives 5 ingested, 1 failed.

Because the row is flagged when it is inserted, P5 never picks it up; P5 processes Pending rows only. **P5 step 1e still applies to the other unusable cases**, for example no usable type or direction.

### 5. Batch status

| Status | When |
|---|---|
| `Succeeded` | No failed rows, including an empty file. |
| `PartiallyFailed` | Some rows failed. |
| `Failed` | Every row failed, or the whole run failed. |

A whole-run failure means the adapter threw (for example, the file can't be read), the provider has no registered adapter, or the save failed. In that case:

- no raw rows are stored;
- the batch records the exception message in `ErrorSummary`;
- the service returns the result and doesn't throw.

Cancellation also marks the batch Failed, but is rethrown. The batch is saved as `Running` before the adapter is called, so a run that dies half-way stays visible.

### 6. Adapter resolution: keyed DI

`GenericCsvProvider` is registered in `AddRobeemInfrastructure` as `AddKeyedSingleton<IAttendanceProvider, GenericCsvProvider>("generic-csv")`. `IngestionService` resolves the adapter with `IServiceProvider.GetKeyedService<IAttendanceProvider>(integration.Provider.ProviderKey)`.

This is the one place a service locator is used. The key is only known at run time, so it can't be injected. The rule is now in CLAUDE.md §4.

### 7. `RecordHash`

SHA-256, lower-case hex, of `"{IntegrationId}\n{sanitized payload}"`. Two identical rows in one file get the same hash, which is expected: raw storage keeps duplicates.

### 8. Other field values

- **`SourceSystem`** is the provider key.
- **`ApiRequestReference`** is not set: a manual upload has no API request id, and the uploaded file name is not kept, because the service signature takes a file path only.

### 9. CSV rules

| Situation | What happens |
|---|---|
| File format | RFC 4180, with a header row. Quoted fields may hold commas, `""` and line breaks. A UTF-8 BOM is accepted. |
| Raw payload | Each row is a JSON object of header to text. |
| Header matching | Ignores case and punctuation. The accepted header names are listed on `GenericCsvProvider`. |
| Blank lines | Skipped. |
| More cells than headers | The extra cells are kept as `column{n}`. |
| Duplicate or blank headers | Renamed `column{n}`. |
| `DateRange` | Ignored: an upload is taken whole. |
| Validate / authenticate | Both succeed trivially: there is no connection. |
| Device and employee lists | Empty: a CSV file has none. |
| `GetTransactionChangesAsync` | Throws `NotSupportedException`: manual upload only. |

### 10. Sanitizer failure

If `IRawPayloadSanitizer` throws, the row is still stored, with:

- `RawPayload` set to `{}`;
- `ProcessingStatus` set to Failed;
- the error recorded in `ProcessingError`.

This can't happen with the CSV adapter, which always produces valid JSON.

### 11. What the Api returns

- **404** when the integration doesn't exist.
- **400** when the file is empty.
- **200** with the counts otherwise, even when some rows failed.

## Not done

- **Inactive providers and paused integrations are accepted.** The prompt doesn't say to reject them.
- **No Web screen.** Not asked for.
- **`ErrorSummary` holds the raw exception message.** That can expose internal detail to the Api caller. Revisit with P12 (security).

## Verification

- **Build:** `dotnet build` finishes with 0 warnings and 0 errors.
- **Format:** `dotnet format --verify-no-changes` passes.
- **Tests:** all 68 pass. The new file is `tests/robeem.Tests/Attendance/IngestionServiceTests.cs`, with 4 tests:
  - Five rows, one of them malformed, are all stored, with counts 5/1, status PartiallyFailed, and the template column removed.
  - Device resolution and UTC instants are correct: wall-clock time via the device's time zone, an explicit offset, and `Z`.
  - When the adapter fails, the batch is marked Failed and nothing is thrown.
  - An unknown integration returns null.
- **End to end on PostgreSQL:** I inserted a provider, integration (id 1) and device (`DEV-1`, Asia/Beirut) into `robeem_demo` with `psql`, then uploaded the sample through the running Api.
  - The upload returned HTTP 200: `{"batchId":1,"status":"PartiallyFailed","recordsIngested":5,"recordsFailed":1}`.
  - An unknown integration returned 404.
  - The stored UTC instants are correct, and no payload contains the template column.
  - This also confirms on Npgsql that P2's UTC conversion stores a `+03:00` value without error.
  - **Those smoke-test rows are still in `robeem_demo`:** a provider, an integration named "HQ uploads (P4 smoke test)", a device, batch 1 and 5 raw rows. Delete them with SQL if they are unwanted; the application can't delete raw rows. The `generic-csv` provider row will not conflict with P15's seed contributor, which only adds what is missing.
