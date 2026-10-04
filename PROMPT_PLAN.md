# Attendance & Time Clock Integration — Prompt Plan

A sequence of prompts to paste into Claude Code, **one at a time, in order**, to implement the feature inside `robeem.attendance`. Each prompt is self-contained (states its own context and definition of done) but assumes `CLAUDE.md` is present at the repository root and has already been read — especially §4 (where each new type goes, by layer) and §9 (solution layout and the project-dependency rules).

## Status as of 2026-10-04 (rewritten against the real repository)

- [x] **P0 — Discovery.** Complete. The host robeem codebase was never made available — the developer received only the 8 shared entity files — so the product owner decided (2026-10-04) that `robeem.attendance` is a **standalone solution**, not an addition to a shared monolith. Every open question has a recorded decision in `CLAUDE.md` (§2.2, §2.3, §2.8, §3.2, §7) and the full reasoning in `DISCOVERY_NOTES.md`. There is nothing left to run for P0.
- [x] **Foundation.** Complete. The 8 supplied entity files live in `src/robeem.Domain` (namespace `robeem.DataModel`, unchanged); every type they depend on but that was never supplied (`SmartEnum`, `ISeedData`, `IWorkflowEntity`, `ITenantScoped`, `Set`, `EntityPropertyExtensions`) was written from scratch to match exactly what the 8 files require; `Profile` (the employee entity `TimeScheduleEmployee.Employee` points at) was added with HR screens; `RobeemDbContext` implements database-per-tenant plus a defense-in-depth `TenantId` column on every table; PostgreSQL replaced the initial SQL Server choice; 37 tests pass against both PostgreSQL and the SQLite test harness.
- [x] **Layering.** Complete. The solution is split into `robeem.Domain` / `robeem.Contracts` / `robeem.Application` / `robeem.Infrastructure` / `robeem.Api` / `robeem.Web` (+ `robeem.Analyzers`, a Roslyn suppressor project), with `robeem.Web` talking to `robeem.Api` only over HTTP rather than opening its own database connection. This replaced the original plan's implicit assumption of one `robeem.DataModel` project with everything else layered loosely around it.
- [ ] **P1–P15 below.** Fully rewritten in this revision to match what's actually in the repository: real project names, PostgreSQL (not SQL Server), database-per-tenant + `TenantId` (not a bare shared-schema `TenantId` column), no repository layer (`IRobeemDbContext` is injected directly into services, the same way `EmployeeService` does it), and the concrete decisions already recorded in `CLAUDE.md` §2–§3 (Module.Attendance, the `_camelCase`/SA1300 StyleCop resolution, the `AllInclusive` definition). Earlier drafts of these prompts were written before the solution existed and assumed a shared `robeem.DataModel` project inside robeem's monolith — that assumption is gone from every prompt below.

**Static analysis needs no per-prompt setup.** `Directory.Build.props` at the repository root already adds `StyleCop.Analyzers`, the root `.editorconfig`/`stylecop.json`, and the `robeem.Analyzers` brand-name suppressor to every project automatically (CLAUDE.md §7.1). Don't add a `PackageReference` for it anywhere, and don't repeat the setup steps earlier drafts of Prompt 1 included — a new project is analyzed simply by existing under the repository root.

**Definition of Done, every prompt, unless stated otherwise** (CLAUDE.md §7.5): `dotnet build robeem.attendance.slnx` is clean (zero errors, zero warnings); `dotnet test robeem.attendance.slnx` is green; `dotnet format robeem.attendance.slnx --verify-no-changes` passes. Each prompt below adds the feature-specific checks on top of this baseline.

---

## Prompt 0 — Codebase discovery (historical — already run, do not re-run)

Kept for traceability only. The original discovery prompt asked 8 questions about an assumed existing host codebase, plus a 9th about StyleCop. The host was never available; the product owner made the calls instead, and they are now the binding decisions in `CLAUDE.md`:

| # | Question | Decision | Where it lives |
|---|---|---|---|
| 1 | Multi-tenancy | Database per tenant, plus `TenantId` on every entity (CLR property via `ITenantScoped`, or an EF shadow property for the 8 supplied files) | CLAUDE.md §2.8 |
| 2 | `SmartEnum.Module` | Defined in this solution as a plain enum: `HR`, `Attendance` | CLAUDE.md §2.2 |
| 3 | `ISeedData` | Marker consumed by `SeedDataRunner` via per-tenant `ISeedContributor`s | CLAUDE.md §2.3 |
| 4 | DbContext / repository | `RobeemDbContext` (EF Core 10, PostgreSQL/Npgsql), `IEntityTypeConfiguration` classes, no repository layer | CLAUDE.md §9 |
| 5 | Background jobs | None yet — deferred to P10 | CLAUDE.md §2.8 |
| 6 | Generic UI/API layer | Indexer helpers implemented and tested; no generic grid — screens are explicit MVC views | DISCOVERY_NOTES.md Q6 |
| 7 | `TimeScheduleTeam.AllInclusive` | The schedule's default team, containing every employee assigned to the schedule | CLAUDE.md §3.2 |
| 8 | Commands | Real values recorded | CLAUDE.md §5 |
| 9 | StyleCop | `robeem` is exempted from SA1300 by a Roslyn suppressor; everything else in the original plan's tiering holds | CLAUDE.md §7 |

Full reasoning, evidence, and what was searched for and not found: `DISCOVERY_NOTES.md` at the repository root.

---

## Prompt 1 — Catalog and configuration entities

```
Implement the catalog/configuration entities for the Attendance integration, per CLAUDE.md
§4 (where each layer's Attendance code goes) and the Business and Technical Study §6.2 (for
field-naming conventions only — these four entities are not in that section), §12.1, and §13.2.

Static analysis needs no setup: Directory.Build.props already wires StyleCop.Analyzers,
.editorconfig and stylecop.json into every project in the solution, including any you add.

1. Entities — create in src/robeem.Domain/Attendance (namespace robeem.DataModel, same as the
   HR entities), each following CLAUDE.md §2 conventions exactly: [Serializable] then
   [Table(nameof(ClassName), Schema = nameof(SmartEnum.Module.Attendance))] (using SmartEnum =
   robeem.System.Contract.SmartEnum; placed after the System usings — see §2.2), the XId/X?
   navigation pairing (§2.4), and implementing ITenantScoped (a `public string TenantId { get;
   set; } = string.Empty;` property with [Required][MaxLength(ITenantScoped.TenantIdMaxLength)]
   — do not set it by hand anywhere; RobeemDbContext stamps it, per §2.8):

   a. AttendanceProvider : Entity, ISeedData, ITenantScoped
      ProviderKey (string, required, unique per tenant), DisplayName (string, required),
      SupportedTransports (string — document the chosen shape, e.g. a comma-separated list or an
      owned JSON value; do not add a new SmartEnum-style type without checking §2.2 first),
      SupportsWebhooks (bool), SupportsIncrementalSync (bool), IsActive (bool, default true).
      Title => DisplayName.

   b. AttendanceIntegration : Entity, ITenantScoped
      ProviderId (FK -> AttendanceProvider) / Provider (nav), DisplayName (string),
      ConnectionConfigRef (string — a reference/key into secrets storage, never a raw secret;
      this solution has no secrets-storage mechanism yet, so store a plain string and flag it
      TODO for P12, which is where authentication/secrets gets decided),
      PollingScheduleCron (string, nullable), SyncCursor (string, nullable — the high-water
      mark, provider-defined shape), LastSuccessfulSyncAt (DateTime?, nullable — stored as
      `timestamp without time zone` automatically, see CLAUDE.md §2.8; don't write a UTC
      DateTimeKind value to it), Status (string, enum-backed: Active/Paused/Failed),
      HealthStatus (string, enum-backed).

   c. AttendanceDevice : Entity, ITenantScoped
      IntegrationId (FK -> AttendanceIntegration) / Integration (nav), ExternalDeviceId
      (string, required), Name (string), DeviceType (enum: Biometric/Facial/RFID/Mobile/
      Cloud/OnPrem), TimeZoneId (string, IANA id), LastSeenAt (DateTime?).

   d. AttendancePolicy : Entity, ISeedData, ITenantScoped
      TimeScheduleId (FK -> TimeSchedule, nullable — null means tenant-wide default, per
      CLAUDE.md §3) / TimeSchedule (nav, nullable), RoundingRuleMinutes (int),
      LateGraceMinutes (int), EarlyDepartureGraceMinutes (int), OvertimeThresholdMinutes (int),
      RequiresOvertimePreApproval (bool), CorrectionApprovalWorkflow (string, enum-backed:
      Manager/HR/Both), UnlabeledPunchInterpretationMode (string, enum-backed:
      Off/Sequential/RulesEngine, default Off per the study's MVP recommendation),
      DuplicateTimeWindowSeconds (int, default 120).

2. Add a DbSet<T> for each entity to BOTH robeem.Application/Persistence/IRobeemDbContext.cs
   and robeem.Infrastructure/Persistence/RobeemDbContext.cs (the DbSet pattern is
   `public DbSet<T> Ts => Set<T>();`, matching the existing Profiles/TimeSchedules properties
   in both files exactly).

3. EF configuration — add an IEntityTypeConfiguration<T> per entity under
   src/robeem.Infrastructure/Persistence/Configurations/Attendance (namespace
   robeem.Infrastructure.Persistence.Configurations.Attendance). RobeemDbContext.OnModelCreating
   already calls modelBuilder.ApplyConfigurationsFromAssembly, so no extra registration step is
   needed — adding the class is enough. Follow the existing ProfileConfiguration.cs /
   TimeScheduleEmployeeConfiguration.cs pattern (src/robeem.Infrastructure/Persistence/
   Configurations/HR) for style. Composite unique indexes start with TenantId, e.g.:
   builder.HasIndex(p => new { p.TenantId, p.ProviderKey }).IsUnique(); and
   builder.HasIndex(d => new { d.TenantId, d.IntegrationId, d.ExternalDeviceId }).IsUnique();
   on AttendanceDevice.

4. ISeedData is a marker only (CLAUDE.md §2.3) — applying it to AttendanceProvider and
   AttendancePolicy is compile-safe and does nothing until an ISeedContributor actually seeds
   them, which is Prompt 15's job, not this one. Do not write a seed contributor in this prompt.

5. Generate the migration from the repository root:
   dotnet ef migrations add AddAttendanceCatalog --project src/robeem.Infrastructure
     --startup-project src/robeem.Api --output-dir Persistence/Migrations
   Confirm it applies by running the Api in Development (it migrates every configured tenant
   database at startup, per CLAUDE.md §5) against local PostgreSQL, and by running the test
   suite (which applies migrations against in-memory SQLite via TestDatabase).

Do not implement any service/controller/Web code yet — entities, DbSets, EF configuration and
migration only. Write a short ATTENDANCE_P1_NOTES.md at the repo root summarizing any field you
had to deviate on from this spec and why.
```

**Definition of done**: four entities compile; each implements `ITenantScoped`; migration generates and applies cleanly against both a fresh PostgreSQL tenant database and the SQLite test harness; unique indexes exist as specified; no service/controller/Web code yet.

---

## Prompt 2 — Raw ingestion storage

```
Implement raw attendance storage per the Business and Technical Study §6.2 and §6.3, CLAUDE.md
§1 (invariant 2: immutability) and §4.

1. Entities (src/robeem.Domain/Attendance, same conventions as Prompt 1):

   a. AttendanceProcessingBatch : SystemEntity, ITenantScoped
      IntegrationId (FK -> AttendanceIntegration, nullable for cross-integration reprocess
      runs) / Integration (nav, nullable), BatchType (string, enum-backed:
      InitialSync/IncrementalSync/Reprocess/ManualUpload/Reconciliation), StartedAt (DateTime),
      CompletedAt (DateTime?), Status (string, enum-backed: Running/Succeeded/Failed/
      PartiallyFailed), RecordsIngested (int), RecordsFailed (int), ErrorSummary (string,
      nullable). [NotMapped] public override string Title => PartitionName; per §2.5.

   b. RawAttendanceTransaction : SystemEntity, ITenantScoped
      IntegrationId (FK) / Integration (nav), DeviceExternalId (string, nullable), DeviceId (FK
      nullable) / Device (nav, nullable), ExternalTransactionId (string, nullable),
      ExternalEmployeeId (string, nullable), TransactionTimestampRaw (string),
      TransactionTimestampParsed (DateTimeOffset?), TransactionTypeRaw (string, nullable),
      DirectionRaw (string, nullable), RawPayload (string — map as a plain unbounded string
      property with no [MaxLength]; Npgsql maps that to `text` by default and SQLite's test
      harness to TEXT, so no provider-specific column type is needed for P2. Only reach for
      `.HasColumnType("jsonb")` behind `if (Database.IsNpgsql())`, mirroring the DateTime
      pattern in RobeemDbContext.OnModelCreating, if a later prompt needs to query inside the
      JSON — don't add it speculatively here), SourceSystem (string), ImportBatchId (FK ->
      AttendanceProcessingBatch) / ImportBatch (nav), ApiRequestReference (string, nullable),
      RecordReceivedAt (DateTimeOffset), ProcessingStatus (string, enum-backed:
      Pending/Normalized/Failed/Skipped), ProcessingError (string, nullable), RecordHash
      (string, indexed), RetentionStatus (string, enum-backed: Active/Archived/PendingDeletion).
      [NotMapped] Title override per §2.5.

2. Add both DbSets to IRobeemDbContext and RobeemDbContext, and an EF configuration for each
   under Infrastructure/Persistence/Configurations/Attendance, same pattern as Prompt 1.

3. Immutability guard. There is no repository layer in this solution (CLAUDE.md §9 — services
   use IRobeemDbContext directly), so "no Update/Delete method on a repository" is not how this
   gets enforced here. Instead, extend RobeemDbContext the way it already enforces tenant
   isolation: in RobeemDbContext.ApplyTenantAndAuditRules (robeem.Infrastructure/Persistence/
   RobeemDbContext.cs), add a check alongside the existing EntityState.Modified/Deleted branch
   that throws InvalidOperationException("RawAttendanceTransaction is immutable.") when
   entry.Entity is RawAttendanceTransaction and entry.State is Modified or Deleted. This runs
   inside the same SaveChanges/SaveChangesAsync override that already stamps TenantId, so every
   write path is covered without a new abstraction.

4. Add the field-allowlist step from CLAUDE.md invariant 10: IRawPayloadSanitizer in
   src/robeem.Application/Attendance, with one method (e.g. Sanitize(string rawJson) or an
   equivalent taking a dictionary, your call) that strips any field whose name matches a
   configurable deny-list (default: anything containing "template", "biometric", "fingerprint",
   "faceprint") before RawPayload is persisted. This is a pure function with no EF/DbContext
   dependency — unit test it standalone, registered in ApplicationServiceCollectionExtensions
   .AddRobeemApplication like IEmployeeService is.

5. Generate the migration (same dotnet ef command shape as Prompt 1, migration name
   AddAttendanceRawStorage). Write unit tests under tests/robeem.Tests/Infrastructure/Attendance
   (mirrors how TenantIsolationTests.cs sits in tests/robeem.Tests/Infrastructure) using
   TestDatabase, proving: (a) a RawAttendanceTransaction can be inserted, (b) updating or
   deleting one throws InvalidOperationException, (c) IRawPayloadSanitizer strips a
   template-like field and leaves everything else untouched (this one can live under
   tests/robeem.Tests/Attendance since it needs no database).
```

**Definition of done**: migration applies to PostgreSQL and SQLite; the three tests above pass; a search confirms no code path calls `Update`/`Remove` on `RawAttendanceTransaction` anywhere in the solution.

---

## Prompt 3 — Normalized transactions

```
Implement the normalized layer per the Business and Technical Study §7.3 and §10.3 (composite
duplicate key), and CLAUDE.md §1 invariant 4.

1. NormalizedAttendanceTransaction : SystemEntity, ITenantScoped (src/robeem.Domain/Attendance):
   EmployeeId (FK -> Profile, nullable until resolved) / Employee (nav, nullable — named
   Employee per CLAUDE.md §2.4 even though the type is Profile), AttendanceDate (DateTime,
   date-only usage), EventTimestampUtc (DateTimeOffset), EventTimestampLocal (DateTimeOffset),
   TimeZoneId (string), NormalizedEventType (string, enum-backed:
   CheckIn/CheckOut/BreakOut/BreakIn/Unknown/Other), OriginalEventType (string, nullable),
   DeviceId (FK nullable) / Device (nav, nullable), IntegrationId (FK) / Integration (nav),
   RawTransactionId (FK -> RawAttendanceTransaction, required) / RawTransaction (nav),
   EventSource (string, enum-backed: Device/MobileApp/WebPortal/Manual), ConfidenceStatus
   (string, enum-backed: Certain/Inferred/Ambiguous), ProcessingStatus (string, enum-backed:
   Pending/Processed/Excluded), DuplicateStatus (string, enum-backed:
   Unique/DuplicateConfirmed/DuplicateCandidate), DuplicateOfTransactionId (FK nullable,
   self-referencing) / DuplicateOfTransaction (nav, nullable), ExceptionStatus (string,
   enum-backed: None/FlaggedInvalidSequence/FlaggedUnrecognizedEmployee/
   FlaggedUnrecognizedDevice).

   Also add, as a plain persisted property (not a SQL computed column — see point 2):
   DuplicateBucketTimestampUtc (DateTimeOffset), the EventTimestampUtc rounded down to
   AttendancePolicy.DuplicateTimeWindowSeconds, computed in application code before the row is
   added (Prompt 5 is where this gets populated; for this prompt, just define the column and
   let Prompt 5 fill it).

2. Composite duplicate index (study §10.3, §13.4): the original plan noted SQL Server can't
   easily index a bucketed expression; that's moot now — this solution computes the bucket in
   C# (point 1) and stores it as an ordinary column, so the unique index is a normal composite
   index, no computed-column DDL needed:
   builder.HasIndex(n => new { n.TenantId, n.EmployeeId, n.DeviceId, n.NormalizedEventType,
   n.DuplicateBucketTimestampUtc }).IsUnique();
   Treat the unique constraint as a race-condition backstop, not the primary duplicate-detection
   mechanism — Prompt 5's normalization service checks for an existing row with the same key
   BEFORE inserting and sets DuplicateStatus explicitly; only a genuine concurrent race should
   ever hit the index. When it does, catch DbUpdateException the way EmployeeService.SaveAsync
   already does for the EmployeeNumber unique index (robeem.Application/HR/Employees/
   EmployeeService.cs) and convert it into a DuplicateConfirmed row rather than letting it
   bubble up.

3. Add the DbSet and EF configuration (Infrastructure/Persistence/Configurations/Attendance),
   generate the migration (AddNormalizedAttendanceTransactions).

4. Write unit tests under tests/robeem.Tests/Infrastructure/Attendance using TestDatabase,
   proving: a normal (non-duplicate) insert succeeds and is marked Unique; two rows for the
   same employee/device/event type/bucket produce one Unique + one DuplicateConfirmed (not an
   uncaught constraint violation); DuplicateOfTransactionId on the second row points at the
   first.
```

**Definition of done**: migration applies; the three scenarios above pass as tests.

---

## Prompt 4 — Provider abstraction and first adapter

```
Implement the vendor-abstraction seam per the Business and Technical Study §5.3 and §12.1-
§12.2, and CLAUDE.md invariant 9.

1. Define IAttendanceProvider in src/robeem.Application/Attendance (namespace
   robeem.Application.Attendance) with exactly these members: ValidateConnectionAsync,
   AuthenticateAsync, RetrieveDevicesAsync, RetrieveEmployeesAsync,
   RetrieveAttendanceTransactionsAsync, GetTransactionChangesAsync — signatures per study §5.3.
   Define the supporting DTOs (AttendanceIntegrationConfig, ConnectionValidationResult,
   AuthResult, ExternalDeviceDto, ExternalEmployeeDto, AttendanceBatchResult, DateRange,
   SyncCursor) as plain records in the same folder. These are internal Application-layer models,
   NOT robeem.Contracts DTOs (Contracts is for Api/Web transport only, per CLAUDE.md §9) and NOT
   Entity subclasses — they must not reference robeem.DataModel or EF Core at all.

2. Implement GenericCsvProvider : IAttendanceProvider in src/robeem.Infrastructure/Attendance/
   Providers (Infrastructure implements the Application-defined port, the same direction as
   RobeemDbContext implementing IRobeemDbContext). Its RetrieveAttendanceTransactionsAsync
   reads a CSV file path from AttendanceIntegrationConfig and returns one AttendanceBatchResult
   entry per row, each carrying the raw row (serialized to JSON) as the "raw payload" — do not
   parse or interpret the row's meaning here, that happens in normalization (Prompt 5). Its
   GetTransactionChangesAsync can throw NotSupportedException for now (CSV has no incremental
   concept) — document that this provider is manual-upload-only. Register it in
   InfrastructureServiceCollectionExtensions.AddRobeemInfrastructure, keyed or resolved the same
   way the study's multi-provider design implies (your call — a keyed DI registration by
   ProviderKey is the natural fit given .NET's keyed services support; document the choice).

3. Implement IIngestionService / IngestionService in src/robeem.Application/Attendance with one
   method, IngestManualUploadAsync(integrationId, filePath, CancellationToken), that: creates an
   AttendanceProcessingBatch (BatchType=ManualUpload) via IRobeemDbContext, calls the provider
   adapter, sanitizes each payload via IRawPayloadSanitizer (Prompt 2), writes
   RawAttendanceTransaction rows, and marks the batch Succeeded/Failed/PartiallyFailed. Register
   it in ApplicationServiceCollectionExtensions.AddRobeemApplication next to IEmployeeService.

4. Add an AttendanceIngestionController in src/robeem.Api/Controllers (namespace
   robeem.Api.Controllers, route api/attendance/ingestion), following the EmployeesController
   pattern exactly: [ApiController], constructor-injects IIngestionService, accepts an
   IFormFile for the manual-upload endpoint, returns robeem.Contracts DTOs (add an
   AttendanceIngestionResult or similar under src/robeem.Contracts/Attendance), never an entity.
   The request automatically carries the caller's tenant — TenantResolutionMiddleware has
   already run by the time the controller executes, same as EmployeesController; no new
   tenancy wiring is needed here.

Write an integration test under tests/robeem.Tests/Attendance that feeds a small sample CSV
(3-5 rows, include one row with a deliberately malformed timestamp) through IngestionService
and asserts: all rows land in RawAttendanceTransaction regardless of validity (invariant 1),
the batch's RecordsIngested/RecordsFailed counts are correct, and no exception escapes the
service for the malformed row.
```

**Definition of done**: integration test passes; a raw row exists for every input row including the malformed one; `IAttendanceProvider` and its DTOs have zero references to `robeem.DataModel`, EF Core, or `robeem.Contracts`.

---

## Prompt 5 — Normalization engine

```
Implement the normalization engine per the Business and Technical Study §7 and §10.3-§10.4.

1. Build INormalizationService.NormalizeBatchAsync(batchId, CancellationToken) in
   src/robeem.Application/Attendance that, for every RawAttendanceTransaction with
   ProcessingStatus=Pending in the given batch (via IRobeemDbContext):

   a. Resolves ExternalEmployeeId -> Profile. No employee-identifier-mapping mechanism exists
      yet anywhere in this solution (Profile has no external-id column) — add a simple
      ExternalEmployeeIdMapping entity (Entity, ITenantScoped: IntegrationId, ExternalEmployeeId,
      EmployeeId/Employee nav, unique on (TenantId, IntegrationId, ExternalEmployeeId)) as part
      of this prompt rather than guessing at a mechanism that was never built.
   b. Resolves DeviceExternalId -> AttendanceDevice via AttendanceDevice.ExternalDeviceId.
   c. Maps TransactionTypeRaw/DirectionRaw to a NormalizedEventType using a per-provider mapping
      strategy (ProviderKey-keyed) — this is the per-vendor piece called out in invariant 9; do
      not hardcode vendor-specific logic outside this mapping layer.
   d. If employee or device cannot be resolved: still create the NormalizedAttendanceTransaction
      row (do not skip it), with ExceptionStatus set to FlaggedUnrecognizedEmployee or
      FlaggedUnrecognizedDevice respectively.
   e. If the raw payload is structurally unusable (unparseable timestamp, no usable
      type/direction at all): do NOT create a normalized row; instead set the raw row's
      ProcessingStatus=Failed with ProcessingError populated (study §6.3 — the one legitimate
      "raw row -> zero normalized rows" case).
   f. Computes DuplicateBucketTimestampUtc (Prompt 3) from EventTimestampUtc and
      AttendancePolicy.DuplicateTimeWindowSeconds (resolve the applicable policy the same way
      Prompt 7 will — TimeScheduleId-scoped with a tenant-wide default), checks for an existing
      row with the same (TenantId, EmployeeId, DeviceId, NormalizedEventType,
      DuplicateBucketTimestampUtc) before inserting, and sets DuplicateStatus accordingly
      (Unique, or DuplicateConfirmed pointing at the existing row via DuplicateOfTransactionId).
   g. Sets the raw row's ProcessingStatus=Normalized on success.

2. Asynchronous trigger (invariant 3: never inline with ingestion). No background-job
   infrastructure exists yet anywhere in this solution — CLAUDE.md §2.8 explicitly defers that
   decision to Prompt 10. Rather than block this prompt on a scheduler choice that isn't due
   yet, add a minimal AttendanceNormalizationHostedService (a BackgroundService in
   src/robeem.Api) that polls, on a short interval, for AttendanceProcessingBatch rows with
   unnormalized raw transactions, iterating ITenantRegistry.Tenants and running each tenant's
   work inside ITenantScopeFactory.CreateScope(tenant.Id) — never share one DbContext across
   tenants (CLAUDE.md §2.8). This satisfies the invariant today; Prompt 10 may replace the
   polling loop with Hangfire or another scheduler without changing NormalizeBatchAsync itself.
   IngestionService (Prompt 4) must NOT call NormalizeBatchAsync directly — only the hosted
   service does.

Write unit tests under tests/robeem.Tests/Attendance (service logic against TestDatabase)
covering: a normal resolvable punch, an unresolvable employee, an unresolvable device, a
structurally invalid timestamp (zero normalized rows produced, raw marked Failed), and a
duplicate pair (one Unique, one DuplicateConfirmed) — reuse the Prompt 3 duplicate scenario as
a base, driven through NormalizeBatchAsync this time instead of direct inserts.
```

**Definition of done**: all five scenarios pass as tests; `IngestionService` has no direct call to `NormalizeBatchAsync`; raw rows end in `Normalized` or `Failed`, never left `Pending` after the hosted service's poll completes.

---

## Prompt 6 — Schedule resolution service

```
Implement IScheduleResolutionService per CLAUDE.md §3.1 and §3.2 (both now fully decided — no
"TODO confirm" branches needed) in src/robeem.Application/Attendance.

ResolveShift(int employeeId, DateTime date, CancellationToken) returns a result type
(ResolvedShift: TimeScheduleShiftId, StartTime, EndTime, BreakTime, BreakStartTime,
BreakEndTime, Overtime, IsOvernight, or a NoScheduleConfigured/DayOff outcome — a discriminated
result, not an exception, since both are expected business outcomes) by:

1. Looking up TimeScheduleEmployee for employeeId (via IRobeemDbContext).
   - If none found: return NoScheduleConfigured. Per CLAUDE.md §3.2, every assignment already
     places the employee in a concrete team (currently always the schedule's all-inclusive team
     — see EmployeeService.AssignScheduleAsync), so there is no AllInclusive-fallback branch to
     write here; an employee either has an assignment with a real TeamId, or has none at all.
2. Calling TimeSchedule.GetCycleDay(date) on the resolved TimeSchedule (loaded via
   IRobeemDbContext.TimeSchedules).
3. Looking up TimeScheduleLine for (TimeScheduleId, TeamId, CycleDay).
   - No line for that combination means a scheduled day off (confirmed, CLAUDE.md §3.2 — e.g.
     the seeded "Standard Week" has lines only for cycle days 1-5). Return DayOff.
4. Loading the TimeScheduleShift for the resolved ShiftId.
5. Computing IsOvernight by calling the IsOvernight(this TimeShiftEntity shift) extension method
   that CLAUDE.md §3 says must live in src/robeem.Application/Attendance — write it in this
   prompt if it doesn't exist yet: `Set.ParseTime(shift.EndTime) < Set.ParseTime(shift.StartTime)`.
   Do not add a stored column (§2.6).

Also implement ResolveShiftsInRange(employeeId, DateTime fromDate, DateTime toDate,
CancellationToken) for multi-day lookups (needed by the processing engine's day-boundary/
overnight handling in Prompt 7) by calling ResolveShift per day.

Write unit tests under tests/robeem.Tests/Attendance: a standard Monday-Friday single-team
employee resolves correctly across a full cycle; an employee with no TimeScheduleEmployee row
produces NoScheduleConfigured; a cycle day with no TimeScheduleLine produces DayOff; an
overnight shift (StartTime "22:00", EndTime "06:00") returns IsOvernight=true (Set.TimeSpan
already counts such a shift into the next day, per CLAUDE.md §2.7 — reuse it, don't reimplement
the arithmetic).
```

**Definition of done**: four unit tests pass; the service has zero new entities/migrations — pure read logic over existing tables.

---

## Prompt 7 — Attendance Processing Engine

```
Implement the processing engine per the Business and Technical Study §8 (including the §8.2
worked calculation) and §17.1 (overnight handling), using IScheduleResolutionService (Prompt 6)
and AttendancePolicy (Prompt 1).

1. Create AttendanceRecord : Entity, ITenantScoped and AttendanceDaySummary : Entity,
   ITenantScoped in src/robeem.Domain/Attendance, fields per study §13.2:
   AttendanceRecord: EmployeeId/Employee (Profile), AttendanceDate, TimeScheduleShiftId/
   TimeScheduleShift (nav), ScheduledStart/ScheduledEnd, ActualCheckIn/ActualCheckOut/
   ActualBreakOut/ActualBreakIn (each paired with a Source enum: Normalized/Corrected),
   WorkingHours, BreakDuration, LateMinutes, EarlyDepartureMinutes, OvertimeMinutes, Status
   (string, enum-backed: Calculated/PendingReview/Approved/Rejected), a nullable string column
   for SourceNormalizedTransactionIds (a comma-separated list of ints is sufficient — no need
   for a join table unless a later prompt needs to query it relationally), Version (int).
   AttendanceDaySummary: EmployeeId/Employee, AttendanceDate, TotalWorkingHours,
   TotalOvertimeHours, TotalBreakHours, ShiftCount, ExceptionCount, Status.

2. Add DbSets, EF configurations (Infrastructure/Persistence/Configurations/Attendance), and a
   migration (AddAttendanceRecords), same pattern as prior prompts.

3. Implement IAttendanceProcessingEngine.ProcessEmployeeDayAsync(employeeId, date,
   CancellationToken) in src/robeem.Application/Attendance:
   a. Resolve the expected shift(s) via IScheduleResolutionService (handle overnight: a shift
      starting 22:00 the given date needs normalized events looked up through 06:00+ the next
      calendar date, per study §17.1 — AttendanceDate is assigned by shift start date).
   b. Fetch NormalizedAttendanceTransaction rows for the employee across the relevant window,
      DuplicateStatus=Unique only.
   c. Resolve the applicable AttendancePolicy via TimeScheduleId (falling back to the
      tenant-wide default where TimeScheduleId is null, per CLAUDE.md §3).
   d. Pair CheckIn/CheckOut and BreakOut/BreakIn chronologically; compute WorkingHours and
      BreakDuration using Set.TimeSpan — reuse Set.TimeSpan(start, end, breakStart, breakEnd)
      where the actual punches play the role of the "HH:mm" strings it expects, or work in raw
      TimeSpan/DateTimeOffset arithmetic directly if the actual punches aren't naturally
      "HH:mm" strings; either way don't reimplement the overnight-rollover logic Set.TimeSpan
      already has. Compute LateMinutes = max(0, actual-scheduled-grace); EarlyDepartureMinutes =
      max(0, scheduled-actual-grace); OvertimeMinutes = max(0, working-scheduled), subject to
      AttendancePolicy.RequiresOvertimePreApproval gating the record to PendingReview instead of
      auto-Approved.
   e. Write/update the AttendanceRecord (never destructively — if one already exists for this
      employee/shift, write a new Version and mark the prior one superseded, per study §6.3).
   f. Roll up into AttendanceDaySummary (sum across all of that day's AttendanceRecords, to
      support multi-shift days from the start, per study §17 "multiple shifts in one day").

This method intentionally does NOT raise exceptions for missing punches yet — that's Prompt 8.
For this prompt, assume all expected punches are present and focus purely on the calculation
and the record/summary write.

Write unit tests under tests/robeem.Tests/Attendance reproducing Appendix B Scenarios 1, 2, and
5 from the study verbatim (standard day -> 8:00 worked/1:00 break/0 late/0 early/0 OT; late
arrival with overtime -> 12 min late, 1:23 OT, PendingReview because
RequiresOvertimePreApproval; overnight shift -> AttendanceDate = shift-start date, working
hours computed across midnight).
```

**Definition of done**: three Appendix-B-based unit tests pass with the exact numbers from the study; multi-shift-day rollup produces one `AttendanceDaySummary` aggregating N `AttendanceRecord`s.

---

## Prompt 8 — Exception detection

```
Implement exception detection per the Business and Technical Study §9 and §14.1.

1. Create AttendanceException : Entity, ITenantScoped in src/robeem.Domain/Attendance, fields
   per study §9.3. Add DbSet, EF configuration, migration.

2. Extend IAttendanceProcessingEngine.ProcessEmployeeDayAsync (Prompt 7) to, after attempting
   the pairing step, detect and raise an AttendanceException (ExceptionType + MissingEventType
   set appropriately, Status=Open) for each of:
   - Missing Check In (a CheckOut with no preceding CheckIn)
   - Missing Check Out (a CheckIn with no following CheckOut, after a configurable grace window
     has elapsed — do not raise it the instant the shift ends, per study §9.2)
   - Missing Break Out (a BreakIn with no preceding BreakOut, when the resolved shift's
     BreakTime=true)
   - Missing Break In (a BreakOut with no following BreakIn, when BreakTime=true)
   - Invalid sequence per the study §14.1 table: CheckOut before CheckIn, BreakIn without a
     prior BreakOut, two consecutive CheckIns (apply the duplicate-window check first per §14.1
     — only raise InvalidSequence if the second CheckIn falls outside the duplicate window)

3. When any Open exception exists for an employee/day, the corresponding AttendanceRecord's
   Status must be PendingReview, never auto-Approved — wire this gate explicitly (the concrete
   enforcement point for CLAUDE.md invariant 5 and study §11.3).

4. A day with zero expected punches and zero actual punches must NOT raise an exception if
   IScheduleResolutionService returned DayOff for that date — confirm this guard exists.

5. Note for Prompt 9: IWorkflowEntity exists in this solution (src/robeem.Domain/Core/
   IWorkflowEntity.cs, consumed by Set.isWorkflowEntity) but per DISCOVERY_NOTES.md Q6 there is
   no generic grid/workflow engine that reads it yet — "screens are explicit MVC views" in this
   solution. Do not put it on AttendanceException itself; CLAUDE.md §2.7 says to put it on
   AttendanceCorrection in P9.

Write unit tests under tests/robeem.Tests/Attendance reproducing Appendix B Scenario 3 (missing
Break Out) plus one test each for: missing Check In, missing Check Out (both before and after
the grace window), missing Break In, CheckOut-before-CheckIn, and a DayOff date producing no
exception at all.
```

**Definition of done**: all listed unit tests pass; an `AttendanceRecord` with any `Open` exception for that employee/day is never `Approved`.

---

## Prompt 9 — Corrections and approval workflow

```
Implement the correction workflow per the Business and Technical Study §11.

1. Create AttendanceCorrection : Entity, ITenantScoped, IWorkflowEntity in
   src/robeem.Domain/Attendance, fields per study §11.2. Add DbSet, EF configuration,
   migration.

2. Implement IAttendanceCorrectionService in src/robeem.Application/Attendance with:
   - ProposeCorrectionAsync(exceptionId, proposedValue, justification, requestedByEmployeeId,
     CancellationToken) -> creates an AttendanceCorrection with Status=Pending.
   - DecideCorrectionAsync(correctionId, decidedByUserId, approved, isHrDecision,
     CancellationToken) -> records ManagerDecision or HRDecision per
     AttendancePolicy.CorrectionApprovalWorkflow (Manager/HR/Both); only moves Status to
     Approved once all required approvals per policy are satisfied; on Approved, sets
     Status=Applied, calls IAttendanceProcessingEngine.ProcessEmployeeDayAsync to recompute the
     AttendanceRecord with the corrected value folded in (new Version, per Prompt 7), and sets
     the originating AttendanceException.Status=Closed with the appropriate ResolutionMethod.
   - On rejection: AttendanceCorrection.Status=Rejected, AttendanceException reverts to Open so
     a new correction can be proposed (study §11.1 state diagram).

3. Add an AttendanceCorrectionsController in src/robeem.Api/Controllers (route
   api/attendance/corrections), following the EmployeesController pattern: an employee submits
   a correction against their own open exception; a manager/HR user lists pending corrections
   for their scope and decides. There is no authentication or role system in this solution yet
   (CLAUDE.md §2.8) — do not invent one here. Build the endpoints to take an explicit
   decidedByUserId/requestedByEmployeeId in the request body/route for now, and leave a clear
   TODO pointing at Prompt 12, which is where authentication and RBAC actually get decided and
   added. Do not leave the endpoints open on the assumption that "an RBAC mechanism exists
   somewhere" — DISCOVERY_NOTES.md confirmed there isn't one.

Write unit tests under tests/robeem.Tests/Attendance: propose -> manager-approve ->
AttendanceRecord recalculated and exception Closed (reuse the Prompt 8 missing-Break-Out
scenario, matching study Appendix B Scenario 3's numbers exactly: BreakOut corrected to 12:00,
final working hours recalculated); propose -> manager-reject -> exception reopened; a policy
requiring Manager+HR where only Manager has decided stays Pending, not Approved.
```

**Definition of done**: Appendix B Scenario 3 reproduced end-to-end; dual-approval policy correctly withholds `Approved` until both decisions are in.

---

## Prompt 10 — Scheduled sync, reconciliation, and the job-scheduler decision

```
Implement the synchronization strategy per the Business and Technical Study §12.4. This is also
where CLAUDE.md §2.8 defers the background-job scheduler decision — make it here, don't carry
Prompt 5's polling hosted service forward by default without considering the alternative.

1. Decide the scheduler. The sibling robeem.storage uses Hangfire on PostgreSQL
   (Hangfire.PostgreSql) — a reasonable, proven pattern in this same technology stack, per
   CLAUDE.md §2.8. Evaluate it against keeping Prompt 5's simple BackgroundService polling loop
   now that there are two jobs (normalization, sync) instead of one. Record the decision and
   its rationale in CLAUDE.md §2.8 (replacing "None yet... chosen in P10") before implementing
   either option — don't implement silently and leave the doc stale.

2. Extend AttendanceIntegration-driven ingestion with a scheduled job (using whichever mechanism
   was just decided) that, per active AttendanceIntegration, for every tenant
   (ITenantRegistry.Tenants, each inside its own ITenantScopeFactory.CreateScope(tenant.Id) —
   never share one DbContext across tenants):
   a. Reads SyncCursor/LastSuccessfulSyncAt.
   b. Calls the provider adapter's GetTransactionChangesAsync with a small overlap window
      (configurable, default 5 minutes) subtracted from the stored cursor.
   c. On success: advances SyncCursor/LastSuccessfulSyncAt only after the batch's raw rows are
      durably committed (study §12.4 "only after a batch fully succeeds").
   d. On failure: leaves the cursor untouched, applies Polly retry with exponential backoff and
      jitter, and opens a circuit breaker per integration after a configurable failure
      threshold, flipping AttendanceIntegration.HealthStatus to Unhealthy.

3. Add a separate nightly ReconciliationJob that re-pulls a wider window (default 48 hours)
   than the incremental cursor and relies on the existing duplicate-detection machinery
   (Prompts 3/5) to absorb the overlap harmlessly.

Write tests: a successful sync advances the cursor; a failed sync (mock the provider to throw)
leaves the cursor unchanged and flips HealthStatus; a retried sync after failure re-delivers
overlapping data that lands as DuplicateConfirmed rather than double-processed (verify no
duplicate AttendanceRecord hour-contribution results).
```

**Definition of done**: the scheduler decision is recorded in `CLAUDE.md` §2.8, not just in code; three tests above pass; cursor-advance-only-on-success behavior is explicit and tested.

---

## Prompt 11 — HR/Payroll extraction contract

```
Implement the payroll eligibility gate per the Business and Technical Study §18.3, in
src/robeem.Application/Attendance.

IPayrollAttendanceExtract.GetApprovedRecordsAsync(payPeriodStart, payPeriodEnd,
CancellationToken) — tenant scope comes from IRobeemDbContext.CurrentTenantId / the global
query filter, exactly like every other Attendance query in this solution, so it does NOT take a
tenant parameter — returns AttendanceRecord/AttendanceDaySummary rows where ALL of:
- Status = Approved
- No AttendanceException referencing that employee/date remains Open or PendingApproval
- Any OvertimeMinutes > 0 where AttendancePolicy.RequiresOvertimePreApproval is true has an
  Approved AttendanceCorrection covering it
- AttendanceDate falls within [payPeriodStart, payPeriodEnd] and that period is not already
  marked closed. No Payroll module or PayPeriod concept exists anywhere in this solution yet
  (it was never built) — add a minimal PayPeriod entity (Entity, ITenantScoped: StartDate,
  EndDate, IsClosed) as part of this prompt, scoped to Attendance for now; a real Payroll
  module, if one is ever built, should take over this concept rather than Attendance owning it
  long-term.

This is the ONLY sanctioned read path into attendance data for anything outside this feature.
No architecture-test project exists in this solution yet. Propose adding one (e.g. via
NetArchTest.Rules) under tests/robeem.Tests asserting that no project other than
robeem.Application/robeem.Infrastructure/robeem.Api references RawAttendanceTransaction or
NormalizedAttendanceTransaction directly — this is new tooling, not a reuse of something that
already exists, so say so plainly rather than implying it's already in place.

Write unit tests: an Approved record with no open exceptions is returned; an Approved record
with an Open exception elsewhere for the same employee/date is excluded; an unapproved overtime
record is excluded until the pre-approval correction exists; a record in an already-closed pay
period is excluded.
```

**Definition of done**: four tests pass; the architecture test (if added) fails deliberately when a disallowed reference is temporarily introduced, confirming it actually catches the violation.

---

## Prompt 12 — Authentication, then security hardening

```
CLAUDE.md §2.8 is explicit: "There is no authentication in this solution yet." Every Attendance
endpoint built in Prompts 4 and 9 currently accepts a caller-supplied identity (employeeId,
decidedByUserId) with nothing to verify it. This prompt cannot just "wire role checks onto
existing endpoints" — it must first introduce real authentication, which is a bigger decision
than the original plan assumed. Do not skip straight to RBAC.

1. Decide and implement authentication for robeem.Api. This needs a human decision if one
   hasn't already been made outside this prompt series: ASP.NET Core Identity with cookie auth
   (simplest, fits a solution with no existing IdP), JWT bearer tokens, or an external IdP
   (Azure AD / Entra ID, given the eventual Azure migration in the business context). Record the
   decision and rationale in CLAUDE.md §2.8 before implementing. Whichever is chosen, it must
   end by issuing a `tenant_id` claim that TenantResolutionMiddleware already expects
   (src/robeem.Api/Tenancy/TenantResolutionMiddleware.cs) — that middleware's authenticated-path
   is already written and waiting for this claim to exist; don't change its contract.
2. Once authentication exists, set Tenancy:AllowUnauthenticatedTenantSelection to false in every
   environment except Development (it already defaults that way per CLAUDE.md §2.8 — confirm it
   stays false everywhere real users can reach).
3. Replace the placeholder AttendanceIntegration.ConnectionConfigRef (Prompt 1) with a real
   secrets mechanism. None exists in this solution yet either — propose one (user-secrets is
   already used for connection strings per CLAUDE.md §5, but that's a development-only
   convenience; production needs a real secret store) rather than assuming one is already there.
4. Add an audit log: a minimal AttendanceAuditLog : SystemEntity, ITenantScoped (Actor, Action,
   EntityType, EntityId, Timestamp, Details) — no existing audit-log mechanism exists elsewhere
   in this solution to reuse. Write to it at: every AttendanceCorrection decision, every
   AttendanceException status change, and every direct read of RawAttendanceTransaction or
   NormalizedAttendanceTransaction outside the normalization/processing engine itself.
5. Wire role checks (Employee/Manager/HR Administrator/Payroll Administrator/Integration
   Administrator, per study §16.5) onto every endpoint added in Prompts 4, 9, and 11, using
   ASP.NET Core's standard [Authorize(Roles = "...")] / policy-based authorization against
   whatever claim the chosen auth scheme issues.

Write tests confirming: a correction decision writes an audit entry with the correct actor; an
Employee-role user cannot call the manager/HR decision endpoint; a Payroll Administrator can
call GetApprovedRecordsAsync but not the correction-decision endpoint.
```

**Definition of done**: authentication actually exists and issues a `tenant_id` claim; role-check tests pass (both allow and deny cases); audit entries are written and queryable for the three listed actions.

---

## Prompt 13 — Observability

```
Implement the KPIs and health signals per the Business and Technical Study §17.3. No
metrics/logging infrastructure beyond standard Microsoft.Extensions.Logging (ILogger<T>, used
throughout Infrastructure already, e.g. SeedDataRunner) exists in this solution — don't assume
a metrics package is already wired in; add one only if you confirm it's genuinely needed and
none exists (check the .csproj files first).

Emit/expose: synchronization success rate, failed transaction rate, duplicate rate, missing
event rate, average synchronization delay, and unresolved exception count — all scoped per
AttendanceIntegration (and, implicitly, per tenant, since every query already runs inside the
current tenant's scope). Expose GetIntegrationHealthAsync(integrationId, CancellationToken) in
src/robeem.Application/Attendance returning the composite health view described in study
§17.3's table, backing AttendanceIntegration.HealthStatus.

Write a test confirming GetIntegrationHealthAsync reflects a forced-Unhealthy state after the
Prompt 10 circuit-breaker scenario trips.
```

**Definition of done**: health query test passes; the six KPIs are computable from existing tables without new schema (confirm, and only add new columns/tables if truly unavoidable).

---

## Prompt 14 — Test suite consolidation

```
Review every unit/integration test added across Prompts 1-13. They already live under
tests/robeem.Tests/Attendance (service/engine logic) and tests/robeem.Tests/Infrastructure/
Attendance (persistence-level tests needing TestDatabase) — consolidate within that existing
two-folder split rather than inventing a new structure, matching how HR's tests are organized
today. Add any missing worked-scenario coverage from the study's Appendix B that wasn't already
covered: Scenario 4 (duplicate from integration retry) end-to-end through ingestion ->
normalization -> processing, confirming the duplicate row never contributes hours.

Produce a short TEST_COVERAGE.md at the repository root mapping each of the study's Appendix B
scenarios and §14.1/§9.2 decision-table rows to the specific test(s) that cover it, and flag any
genuine gap rather than claiming coverage that doesn't exist.
```

**Definition of done**: `TEST_COVERAGE.md` exists; every Appendix B scenario has at least one named test; any genuine gap is listed, not papered over.

---

## Prompt 15 — Seed data

```
Implement seeding for AttendanceProvider and AttendancePolicy via the mechanism CLAUDE.md §2.3
already documents: an ISeedContributor (src/robeem.Application/Attendance/Seeding, mirroring
HrSeedContributor in src/robeem.Application/HR/Seeding), registered in
ApplicationServiceCollectionExtensions.AddRobeemApplication alongside HrSeedContributor.
SeedDataRunner already runs every registered ISeedContributor for every tenant database on
startup (Development) and rejects any contributor that stages a change to a type not marked
ISeedData — AttendanceProvider and AttendancePolicy already carry that marker from Prompt 1, so
no changes are needed there.

Contributors must be idempotent (check what's missing before adding, exactly like
HrSeedContributor does for the "Standard Week" schedule).

Seed AttendanceProvider with at minimum: GenericCsvProvider (matching Prompt 4's ProviderKey),
GenericWebhookProvider (stub, SupportsWebhooks=true, for future use), and one or two realistic
named vendors from the study's examples (e.g. a ZKTeco-style biometric provider) marked
IsActive=false until a real adapter exists for them — do not seed a provider as active unless
Prompt 4's adapter (or a later one) actually implements it.

Seed one default tenant-wide AttendancePolicy (TimeScheduleId=null) with the MVP defaults from
study §20.1: UnlabeledPunchInterpretationMode=Off, RequiresOvertimePreApproval configurable but
default false, CorrectionApprovalWorkflow=Manager.
```

**Definition of done**: seed data appears in every tenant database after `dotnet run --project src/robeem.Api` in Development (per CLAUDE.md §5), and in the test suite's seeding tests (mirroring `SeedDataRunnerTests.cs`); no provider is marked active without a corresponding working adapter.
