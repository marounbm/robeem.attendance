# Attendance P1 — catalog/configuration entities: notes

Prompt 1 added `AttendanceProvider`, `AttendanceIntegration`, `AttendanceDevice` and `AttendancePolicy`. It also added their DbSets, their EF configurations and the `AddAttendanceCatalog` migration. This file records where the result differs from, or fills a gap in, the prompt's spec.

## Deviations and choices

| Item | Spec | What was done | Why |
|---|---|---|---|
| Enum-backed fields (`Status`, `HealthStatus`, `DeviceType`, `CorrectionApprovalWorkflow`, `UnlabeledPunchInterpretationMode`) | "string, enum-backed" | Each CLR property is typed as a C# enum in `src/robeem.Domain/Attendance`. It is stored as the member **name** (`HasConversion<string>()`, `varchar(32)`). | The column is still a readable string, and code gets compile-time checking. `DeviceType` is also stored by name, for consistency. These are plain enums, not `SmartEnum` members: `SmartEnum` holds only `Module` (§2.2). |
| `SupportedTransports` | shape to be chosen | A comma-separated list of `AttendanceTransport` names (`FileUpload`, `Sftp`, `RestApi`, `Webhook`, `Database`, `DeviceSdk`), `varchar(255)`. `SupportsTransport(AttendanceTransport)` parses it, ignoring spaces and case. | It is the simplest shape that works the same on PostgreSQL and SQLite. A JSON or owned type would need provider-specific mapping. The member list is my own choice; adjust it to the study's §5.3 list if that list differs. |
| `HealthStatus` values | not listed | `Unknown` (default), `Healthy`, `Degraded`, `Unhealthy`. | P10 and P13 set it to `Unhealthy`. `Unknown` covers an integration that has never synced. |
| `Title` | `Title => DisplayName` on the provider | Provider: `Title => DisplayName`. Integration: `Title => DisplayName`. Device: `Title => Name`. All three are `[NotMapped]`, so these tables have no `Title` column. **`AttendancePolicy` keeps the base, stored `Title`**, which is the policy's name. | A policy has no natural label of its own. Deriving one from the schedule would need the navigation loaded. |
| `AttendanceIntegration.DisplayName`, `AttendanceDevice.Name` | type only | Required, max 255. | Each one backs the entity's `Title`, which robeem's generic layer expects to be non-empty. |
| `AttendanceDevice.TimeZoneId` | IANA id | Required, max 64, no default. | Normalization cannot interpret a local punch time without it. A default such as `UTC` would shift punches silently. Validating the id against IANA is left to the service that creates devices. |
| `ConnectionConfigRef` | plain string, TODO P12 | Required (empty by default), max 255, with a `TODO (P12)` remark. | — |
| `SyncCursor` | nullable string | Unbounded `text`. | The provider defines its shape, so a length limit would be arbitrary. |
| Policy uniqueness | not specified | Two filtered unique indexes: `(TenantId, TimeScheduleId) WHERE "TimeScheduleId" IS NOT NULL` and `(TenantId) WHERE "TimeScheduleId" IS NULL`. | This enforces §3: one policy per schedule plus one tenant-wide default. A plain unique index would allow any number of defaults, because NULLs are distinct in it. The filter syntax works on both PostgreSQL and SQLite. |
| Defaults | `IsActive` true, interpretation `Off`, duplicate window 120 | As specified. Also: `Status` = `Active` and `CorrectionApprovalWorkflow` = `Manager`. The minute fields default to 0. | — |
| `ISeedData` | on provider and policy | Applied. No seed contributor (that is P15). | `SeedDataRunner` only runs registered contributors, so the marker alone seeds nothing. This settles the open concern in `DISCOVERY_NOTES.md` §3. |

## Verification

- **Build:** `dotnet build` finishes with 0 warnings and 0 errors.
- **Format:** `dotnet format --verify-no-changes` passes.
- **Tests:** `dotnet test` passes. The run includes the new `AttendanceCatalogPersistenceTests`, which cover:
  - round-trip and tenant stamping;
  - enums stored by name;
  - the single-default-policy rule.
- **Migration on PostgreSQL:** running the Api in Development applied `AddAttendanceCatalog` to both tenant databases, `robeem_demo` and `robeem_acme`.
- **Note:** the test suite does **not** apply migrations, contrary to the prompt. `TestDatabase` builds the schema from the model with `EnsureCreated`. The tests therefore check the model on SQLite. Only the Api run checks the migration file itself.
