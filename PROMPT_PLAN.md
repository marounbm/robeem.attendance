# Attendance & Time Clock Integration — Prompt Plan

A sequence of prompts to paste into Claude Code, **one at a time, in order**, to implement the feature inside the existing robeem codebase. Each prompt is self-contained (states its own context and definition of done) but assumes `CLAUDE.md` is present at the repo root and has already been read.

Do not skip **Prompt 0**. Every later prompt depends on its findings, especially the multi-tenancy answer.

**Static analysis**: per `CLAUDE.md` §7, every `.csproj` this feature touches or creates must build clean under StyleCop.Analyzers, governed by the `.editorconfig` and `stylecop.json` at the repo root. Prompt 0 checks whether the main robeem solution already has its own analyzer configuration (use that instead if so); Prompt 1 is where the StyleCop package reference actually gets added, since that's the first prompt to touch a real `.csproj`. From Prompt 1 onward, "the project builds with zero StyleCop errors" is part of every prompt's Definition of Done, not just a final cleanup pass.

---

## Prompt 0 — Codebase discovery (read-only, no code changes)

```
Before we implement the Attendance & Time Clock Integration feature described in CLAUDE.md,
investigate this codebase and answer the following — do not write or modify any code yet,
this is a discovery pass only. Produce your findings as a markdown file, DISCOVERY_NOTES.md,
at the repo root.

1. Multi-tenancy: how does this application scope data to a tenant today? Look for a
   TenantId column on any entity not shown in CLAUDE.md, a global EF Core query filter
   (HasQueryFilter) in the DbContext, a database-per-tenant or schema-per-tenant connection
   resolver, a tenant accessor service/middleware, or a claims-based tenant resolution.
   Quote the actual mechanism you find, with file/line references.

2. SmartEnum.Module: find robeem.System.Contract.SmartEnum and list every existing Module
   value. Is it a plain enum (requiring a code change + migration to extend) or something
   data-driven? Recommend whether new Attendance entities should use a new Module.Attendance
   or reuse Module.HR, based on how other modules are organized.

3. ISeedData: find where this interface is consumed (a seeding service, a demo-data
   generator, a startup hook). Confirm whether it seeds on every startup, on first-run only,
   or via an explicit command. Confirm it would be safe to implement on a new "catalog" type
   entity like AttendanceProvider.

4. DbContext and repository pattern: find the EF Core DbContext class, confirm how DbSet
   properties are declared and configured (data annotations only, or Fluent API /
   IEntityTypeConfiguration classes too), and how a typical existing HR feature (e.g.
   anything using TimeSchedule) is wired into a service/controller layer. Identify the
   repository/unit-of-work pattern in use, if any.

5. Background jobs: is there an existing job scheduler (Hangfire, Quartz, a custom worker)?
   If yes, show how an existing recurring job is registered, so new ingestion/sync jobs can
   follow the same pattern.

6. Generic UI/API layer: Entity has a reflective indexer (this[string key]) and
   isSystemEntity()/isWorkflowEntity() helpers. Find where these are actually used (a
   generic grid, a generic REST controller, a metadata-driven form renderer). Confirm that a
   new Entity-derived class with correctly named FK/navigation pairs (per CLAUDE.md §2.4)
   will "just work" with this layer, or note any additional registration step required
   (e.g., a metadata attribute, a menu/module registration file, a permission entry).

7. TimeScheduleTeam.AllInclusive: search for any existing code that reads this property.
   Report what you find, even if it's nothing — this determines how
   IScheduleResolutionService (Prompt 6) should treat employees with no explicit
   TimeScheduleEmployee assignment.

8. Confirm real values for the Commands section placeholders in CLAUDE.md §5 (solution
   file name, API project name, data/migrations project name, DbContext class name, test
   project name) and update that section of CLAUDE.md in place with the real values.

9. Static analysis: does the main robeem solution already reference StyleCop.Analyzers
   anywhere (a Directory.Build.props, a solution-level .editorconfig, a .ruleset file, or a
   PackageReference in any existing .csproj)? Search the whole solution tree, not just
   robeem.DataModel. If you find one, quote its configuration and recommend reusing it
   as-is for this feature instead of the configuration shipped in CLAUDE.md §7/.editorconfig/
   stylecop.json. If you find nothing (consistent with the sibling robeem.storage solution,
   which also has none), confirm the CLAUDE.md §7 configuration is safe to adopt unchanged.
   Also confirm, if possible, whether this codebase's private-field convention is `camelCase`
   or `_camelCase` (needed to resolve the SA1309 severity left as "warning" pending this
   answer in the supplied .editorconfig), and update .editorconfig in place once known.

Write DISCOVERY_NOTES.md with a clear, direct answer to each numbered question (cite files),
update CLAUDE.md §5, §2.2/§2.8, and (per question 9) the root .editorconfig with what you
found (replace "unresolved"/"open question"/"UNCONFIRMED" language with the actual answer, or
state plainly that it remains genuinely undecided and needs a human decision), and stop. Do
not proceed to Prompt 1 content in this same prompt.
```

**Definition of done**: `DISCOVERY_NOTES.md` exists and answers all 9 questions with citations; `CLAUDE.md` §5 has real commands; §2.2 and §2.8 state a confirmed answer or an explicit "needs a human decision" flag — never a silent assumption; the StyleCop configuration question (9) is resolved one way or the other — either "reuse the existing solution-wide config" or "the supplied `.editorconfig`/`stylecop.json` are confirmed safe to adopt" — before Prompt 1 adds the package reference.

---

## Prompt 1 — Catalog and configuration entities

```
Before creating any entity, apply the static-analysis setup from CLAUDE.md §7:

0a. If DISCOVERY_NOTES.md (Prompt 0, question 9) found an existing StyleCop.Analyzers
    configuration elsewhere in the robeem solution, reuse it (add a PackageReference to the
    same version in the project you're about to touch, and do NOT copy the root
    .editorconfig/stylecop.json shipped alongside CLAUDE.md — skip to step 0c). Otherwise
    continue with 0b.
0b. Add the StyleCop.Analyzers PackageReference from CLAUDE.md §7.1 to whichever .csproj will
    host these four entities (robeem.DataModel.csproj if that's where DISCOVERY_NOTES.md says
    they belong, or a newly created project otherwise). Confirm the root .editorconfig and
    stylecop.json shipped with CLAUDE.md are present (copy them in if this is the first prompt
    to touch a real project file); update the SA1309 severity per whatever Prompt 0 found.
0c. Run `dotnet build` once with no code changes yet, to confirm the analyzer loads without
    erroring on existing code before you add anything new.

Implement the catalog/configuration entities for the Attendance integration, per CLAUDE.md
§4 and the Business and Technical Study §6.2 (for field-naming conventions only — these four
entities are not in that section, see below), §12.1, and §13.2.

Create, in robeem.DataModel, following CLAUDE.md §2 conventions exactly (base class, table/
schema attributes using the Module value DISCOVERY_NOTES.md confirmed, FK/navigation naming,
Title overrides where needed):

1. AttendanceProvider : Entity, ISeedData
   Fields: ProviderKey (string, required, unique), DisplayName (string, required),
   SupportedTransports (string or flags enum — your call, document the choice),
   SupportsWebhooks (bool), SupportsIncrementalSync (bool), IsActive (bool, default true).

2. AttendanceIntegration : Entity
   Fields: ProviderId (FK -> AttendanceProvider), DisplayName (string), ConnectionConfigRef
   (string — a reference/key into secrets storage, never a raw secret; confirm the real
   secrets-storage mechanism in this codebase first if not already known, otherwise use a
   plain string and flag it as TODO), PollingScheduleCron (string, nullable),
   SyncCursor (string, nullable — the high-water mark, provider-defined shape),
   LastSuccessfulSyncAt (DateTime?, nullable), Status (string or enum: Active/Paused/Failed),
   HealthStatus (string or enum). Add whatever tenant-scoping column/mechanism
   DISCOVERY_NOTES.md confirmed — do not add TenantId if the discovery found a different
   mechanism.

3. AttendanceDevice : Entity
   Fields: IntegrationId (FK -> AttendanceIntegration), ExternalDeviceId (string, required),
   Name (string), DeviceType (enum: Biometric/Facial/RFID/Mobile/Cloud/OnPrem), TimeZoneId
   (string, IANA id), LastSeenAt (DateTime?). Unique index on (IntegrationId, ExternalDeviceId).

4. AttendancePolicy : Entity, ISeedData
   Fields: TimeScheduleId (FK -> TimeSchedule, nullable — null means tenant-wide default,
   per CLAUDE.md §3), RoundingRuleMinutes (int), LateGraceMinutes (int),
   EarlyDepartureGraceMinutes (int), OvertimeThresholdMinutes (int),
   RequiresOvertimePreApproval (bool), CorrectionApprovalWorkflow (enum: Manager/HR/Both),
   UnlabeledPunchInterpretationMode (enum: Off/Sequential/RulesEngine, default Off per the
   study's MVP recommendation), DuplicateTimeWindowSeconds (int, default 120).

For each entity: add an EF Core configuration (Fluent API or IEntityTypeConfiguration,
matching whatever DISCOVERY_NOTES.md found as the dominant style in this codebase), register
the DbSet on the DbContext, and generate the EF Core migration. Do not implement any service/
controller logic yet — entities and migration only.

Write a short ATTENDANCE_P1_NOTES.md summarizing any field you had to deviate on from this
spec and why (e.g., because of a naming collision or an existing convention found in P0), and
note which StyleCop path (0a or 0b above) was taken and why.
```

**Definition of done**: four entities compile, migration generates and applies cleanly against a dev database, unique index exists on `AttendanceDevice`, no service/controller code yet; `dotnet build` (or `dotnet format --verify-no-changes` per CLAUDE.md §7.5) reports zero StyleCop errors for the four new entity files — this StyleCop check is now part of every subsequent prompt's Definition of Done, not repeated in writing each time.

---

## Prompt 2 — Raw ingestion storage

```
Implement raw attendance storage per the Business and Technical Study §6.2 and §6.3, and
CLAUDE.md §1 (invariant 2: immutability) and §4.

Create:

1. AttendanceProcessingBatch : SystemEntity
   Fields: IntegrationId (FK -> AttendanceIntegration, nullable for cross-integration
   reprocess runs), BatchType (enum: InitialSync/IncrementalSync/Reprocess/ManualUpload/
   Reconciliation), StartedAt (DateTime), CompletedAt (DateTime?), Status (enum: Running/
   Succeeded/Failed/PartiallyFailed), RecordsIngested (int), RecordsFailed (int),
   ErrorSummary (string, nullable).

2. RawAttendanceTransaction : SystemEntity
   Fields exactly per study §6.2: IntegrationId, CompanyId (nullable), ProviderKey (string,
   denormalized), DeviceExternalId (string, nullable), DeviceId (FK nullable),
   ExternalTransactionId (string, nullable), ExternalEmployeeId (string, nullable),
   TransactionTimestampRaw (string), TransactionTimestampParsed (DateTimeOffset?),
   TransactionTypeRaw (string, nullable), DirectionRaw (string, nullable), RawPayload
   (string, NVARCHAR(MAX) equivalent — JSON), SourceSystem (string), ImportBatchId (FK ->
   AttendanceProcessingBatch), ApiRequestReference (string, nullable), RecordReceivedAt
   (DateTimeOffset), ProcessingStatus (enum: Pending/Normalized/Failed/Skipped),
   ProcessingError (string, nullable), RecordHash (string, indexed), RetentionStatus (enum:
   Active/Archived/PendingDeletion).

Add a repository (or extend whatever repository pattern P0 found) whose write surface
exposes only Add/AddRange for RawAttendanceTransaction — no Update or Delete method at all.
If the existing generic repository pattern in this codebase exposes a generic Update/Delete
for every entity by default, add an explicit guard (e.g., throw
InvalidOperationException("RawAttendanceTransaction is immutable") from an override, or an
EF Core SaveChanges interceptor that rejects Modified/Deleted EntityState for this type) —
pick whichever matches the existing architecture better and document the choice.

Add the field-allowlist step mentioned in CLAUDE.md invariant 10: a small
IRawPayloadSanitizer with one adapter-facing method that strips any field whose name matches
a configurable deny-list (default: anything containing "template", "biometric", "fingerprint",
"faceprint") before RawPayload is persisted. Wire it as a mandatory call in the (not yet
built) ingestion path — for now, unit test it standalone.

Generate the migration. Write unit tests proving: (a) a RawAttendanceTransaction can be
inserted, (b) attempting to update or delete one throws/fails the way you implemented,
(c) IRawPayloadSanitizer strips a template-like field and leaves everything else untouched.
```

**Definition of done**: migration applies; three unit tests above pass; no Update/Delete path exists for raw rows anywhere in the codebase (grep to confirm).

---

## Prompt 3 — Normalized transactions

```
Implement the normalized layer per the Business and Technical Study §7.3 and §10.3
(composite duplicate key), and CLAUDE.md §1 invariant 4.

Create NormalizedAttendanceTransaction : SystemEntity with fields exactly per study §7.3:
EmployeeId (FK -> Profile, nullable until resolved — name it Employee per CLAUDE.md §2.4),
AttendanceDate (date), EventTimestampUtc (DateTimeOffset), EventTimestampLocal
(DateTimeOffset), TimeZoneId (string), NormalizedEventType (enum: CheckIn/CheckOut/BreakOut/
BreakIn/Unknown/Other), OriginalEventType (string, nullable), DeviceId (FK nullable),
IntegrationId (FK), RawTransactionId (FK -> RawAttendanceTransaction, required),
EventSource (enum: Device/MobileApp/WebPortal/Manual), ConfidenceStatus (enum: Certain/
Inferred/Ambiguous), ProcessingStatus (enum: Pending/Processed/Excluded), DuplicateStatus
(enum: Unique/DuplicateConfirmed/DuplicateCandidate), DuplicateOfTransactionId (FK nullable,
self-referencing), ExceptionStatus (enum: None/FlaggedInvalidSequence/
FlaggedUnrecognizedEmployee/FlaggedUnrecognizedDevice).

Add the composite unique index described in study §10.3 and §13.4:
(TenantId-or-whatever-mechanism-P0-found, EmployeeId, DeviceId, NormalizedEventType,
EventTimestampUtc rounded/bucketed to AttendancePolicy.DuplicateTimeWindowSeconds). Since SQL
Server can't index a "bucketed" expression directly via a simple column, implement this as
either (a) a persisted computed column for the bucket value with a unique index on it, or (b)
an application-level check-then-insert inside a transaction with appropriate isolation —
choose based on what's idiomatic for this codebase (check DISCOVERY_NOTES.md) and document
the tradeoff in a comment above the configuration.

Generate the migration. Write unit tests proving: two normalized rows for the same employee/
device/event type within the duplicate window produce one Unique + one DuplicateConfirmed
row (not a thrown constraint-violation exception bubbling up uncaught), and that the
AttendanceCorrection chain data (DuplicateOfTransactionId) points at the first one.
```

**Definition of done**: migration applies; duplicate-handling unit test passes; a normal (non-duplicate) insert succeeds and is marked `Unique`.

---

## Prompt 4 — Provider abstraction and first adapter

```
Implement the vendor-abstraction seam per the Business and Technical Study §5.3 and §12.1-
§12.2, and CLAUDE.md invariant 9.

1. Define IAttendanceProvider in the Application/Domain layer (not Infrastructure — confirm
   the actual layering/project names from DISCOVERY_NOTES.md) with exactly these members:
   ValidateConnectionAsync, AuthenticateAsync, RetrieveDevicesAsync, RetrieveEmployeesAsync,
   RetrieveAttendanceTransactionsAsync, GetTransactionChangesAsync — signatures per study
   §5.3. Define the supporting DTOs (AttendanceIntegrationConfig, ConnectionValidationResult,
   AuthResult, ExternalDeviceDto, ExternalEmployeeDto, AttendanceBatchResult, DateRange,
   SyncCursor) as plain records/classes in the same layer — these are NOT Entity subclasses,
   they are transport DTOs and must not touch robeem.DataModel.

2. Implement GenericCsvProvider : IAttendanceProvider in Infrastructure. Its
   RetrieveAttendanceTransactionsAsync reads a CSV file path from
   AttendanceIntegrationConfig and returns one AttendanceBatchResult entry per row, each
   carrying the raw row (serialized to JSON) as the "raw payload" — do not parse or
   interpret the row's meaning here, that happens in normalization (Prompt 5). Its
   GetTransactionChangesAsync can return NotSupportedException for now (CSV has no
   incremental concept) — document that this provider is manual-upload-only.

3. Implement an IngestionService with one method, IngestManualUploadAsync(integrationId,
   filePath), that: creates an AttendanceProcessingBatch (BatchType=ManualUpload), calls the
   provider adapter, sanitizes each payload via IRawPayloadSanitizer (Prompt 2), writes
   RawAttendanceTransaction rows, and marks the batch Succeeded/Failed/PartiallyFailed.

4. Wire a minimal endpoint or admin action to call IngestManualUploadAsync for a given
   AttendanceIntegration — match whatever controller/API convention DISCOVERY_NOTES.md found.

Write an integration test that feeds a small sample CSV (3-5 rows, include one row with a
deliberately malformed timestamp) through IngestionService and asserts: all rows land in
RawAttendanceTransaction regardless of validity (per invariant 1), the batch's
RecordsIngested/RecordsFailed counts are correct, and no exception escapes the service for
the malformed row.
```

**Definition of done**: integration test passes; a raw row exists for every input row including the malformed one; `IAttendanceProvider` has zero references to any EF/DataModel type.

---

## Prompt 5 — Normalization engine

```
Implement the normalization engine per the Business and Technical Study §7 and §10.3-§10.4.

Build INormalizationService.NormalizeBatchAsync(batchId) that, for every
RawAttendanceTransaction with ProcessingStatus=Pending in the given batch:

1. Resolves ExternalEmployeeId -> Profile via whatever employee-identifier-mapping
   mechanism exists (check DISCOVERY_NOTES.md; if none exists yet, add a simple
   EmployeeExternalIdMapping lookup table/service here and flag it for review — this is a
   new concern not covered by the existing TimeSchedule entities).
2. Resolves DeviceExternalId -> AttendanceDevice via AttendanceDevice.ExternalDeviceId.
3. Maps TransactionTypeRaw/DirectionRaw to a NormalizedEventType using a per-provider
   mapping table or strategy (ProviderKey-keyed) — this is the per-vendor piece called out
   in CLAUDE.md invariant 9; do not hardcode vendor-specific logic outside this mapping
   layer.
4. If employee or device cannot be resolved: still create the NormalizedAttendanceTransaction
   row (do not skip it), with ExceptionStatus set to FlaggedUnrecognizedEmployee or
   FlaggedUnrecognizedDevice respectively.
5. If the raw payload is structurally unusable (unparseable timestamp, no usable
   type/direction at all): do NOT create a normalized row; instead set the raw row's
   ProcessingStatus=Failed with ProcessingError populated. (Study §6.3 — this is the one
   legitimate "raw row -> zero normalized rows" case.)
6. Applies the composite duplicate check from Prompt 3 and sets DuplicateStatus accordingly.
7. Sets the raw row's ProcessingStatus=Normalized on success.

All of this must run inside whatever background-job mechanism DISCOVERY_NOTES.md found
(invariant 3: asynchronous, never inline with ingestion) — wire NormalizeBatchAsync to run
as a job triggered after IngestionService completes a batch, not called synchronously from
it.

Write unit tests covering: a normal resolvable punch, an unresolvable employee, an
unresolvable device, a structurally invalid timestamp (zero normalized rows produced,
raw marked Failed), and a duplicate pair (one Unique, one DuplicateConfirmed) — reuse the
Prompt 3 duplicate test as a base.
```

**Definition of done**: all five scenarios above have a passing unit test; normalization runs as a background job, not inline; raw rows end in `Normalized` or `Failed`, never left `Pending` after the job completes.

---

## Prompt 6 — Schedule resolution service

```
Implement IScheduleResolutionService per CLAUDE.md §3.1 exactly — this is new glue code,
not a new entity.

ResolveShift(int employeeId, DateTime date) returns a small result type (ResolvedShift:
TimeScheduleShiftId, StartTime, EndTime, BreakTime, BreakStartTime, BreakEndTime, Overtime,
IsOvernight, or a "NoScheduleConfigured"/"DayOff" outcome) by:

1. Looking up TimeScheduleEmployee for employeeId.
   - If none found: based on what DISCOVERY_NOTES.md found about TimeScheduleTeam.
     AllInclusive, either fall back to the AllInclusive team for that employee's default
     TimeSchedule (if such a concept is confirmed to exist) or return
     NoScheduleConfigured — do NOT silently guess if nothing in the codebase confirms the
     AllInclusive fallback behavior.
2. Calling TimeSchedule.GetCycleDay(date) on the resolved TimeSchedule.
3. Looking up TimeScheduleLine for (TimeScheduleId, TeamId, CycleDay).
   - If no line exists for that combination: return DayOff (confirm this interpretation
     against DISCOVERY_NOTES.md §7 findings; if evidence suggests otherwise, implement what
     the evidence shows instead and document the discrepancy).
4. Loading the TimeScheduleShift for the resolved ShiftId.
5. Computing IsOvernight as a derived value (EndTime earlier than StartTime as a time-of-day
   comparison via Set.asTime) — do not add a stored column, per CLAUDE.md §2.6.

Also implement ResolveShiftsInRange(employeeId, DateTime fromDate, DateTime toDate) for
multi-day lookups (needed by the processing engine's day-boundary/overnight handling in
Prompt 7) by calling ResolveShift per day.

Write unit tests: a standard Monday-Friday single-team employee resolves correctly across a
full cycle; an employee with no TimeScheduleEmployee row produces the NoScheduleConfigured
(or confirmed AllInclusive) outcome; a cycle day with no TimeScheduleLine produces DayOff; an
overnight shift (StartTime "22:00", EndTime "06:00") returns IsOvernight=true.
```

**Definition of done**: four unit tests above pass; the service has zero new entities/migrations — pure read logic over existing tables.

---

## Prompt 7 — Attendance Processing Engine

```
Implement the processing engine per the Business and Technical Study §8 (including the §8.2
worked calculation) and §17.1 (overnight handling), using IScheduleResolutionService
(Prompt 6) and AttendancePolicy (Prompt 1).

Create AttendanceRecord : Entity and AttendanceDaySummary : Entity with fields per study
§13.2 (AttendanceRecord: EmployeeId, AttendanceDate, WorkShiftId-rename to
TimeScheduleShiftId, ScheduledStart/End, ActualCheckIn/Out, BreakOut/In each with a Source
enum (Normalized/Corrected), WorkingHours, BreakDuration, LateMinutes,
EarlyDepartureMinutes, OvertimeMinutes, Status enum (Calculated/PendingReview/Approved/
Rejected), a link table or JSON column for SourceNormalizedTransactionIds, Version int;
AttendanceDaySummary: EmployeeId, AttendanceDate, TotalWorkingHours, TotalOvertimeHours,
TotalBreakHours, ShiftCount, ExceptionCount, Status).

Implement IAttendanceProcessingEngine.ProcessEmployeeDayAsync(employeeId, date):
1. Resolve the expected shift(s) via IScheduleResolutionService (handle overnight: a shift
   starting 22:00 the given date needs normalized events looked up through 06:00+ the next
   calendar date, per study §17.1 — AttendanceDate is assigned by shift start date).
2. Fetch NormalizedAttendanceTransaction rows for the employee across the relevant window,
   DuplicateStatus=Unique only.
3. Resolve AttendancePolicy applicable (via TimeScheduleId per CLAUDE.md §3, falling back to
   tenant-wide default per Prompt 1's nullable TimeScheduleId design).
4. Pair CheckIn/CheckOut and BreakOut/BreakIn chronologically; compute WorkingHours,
   BreakDuration, LateMinutes, EarlyDepartureMinutes, OvertimeMinutes exactly per the §8.2
   formulas (gross span minus break = working hours; late = max(0, actual-scheduled-grace);
   early = max(0, scheduled-actual-grace); overtime = max(0, working-scheduled), subject to
   AttendancePolicy.RequiresOvertimePreApproval gating the record to PendingReview instead
   of auto-Approved).
5. Write/update the AttendanceRecord (never destructively — if one already exists for this
   employee/shift, write a new Version and mark the prior one superseded, per study §6.3).
6. Roll up into AttendanceDaySummary (sum across all of that day's AttendanceRecords, to
   support multi-shift days from the start, per study §17 "multiple shifts in one day").

This method intentionally does NOT raise exceptions for missing punches yet — that's Prompt
8. For this prompt, assume all expected punches are present and focus purely on the
calculation and the record/summary write.

Write unit tests reproducing Appendix B Scenarios 1, 2, and 5 from the study verbatim
(standard day -> 8:00 worked/1:00 break/0 late/0 early/0 OT; late arrival with overtime ->
12 min late, 1:23 OT, PendingReview because RequiresOvertimePreApproval; overnight shift ->
AttendanceDate = shift-start date, working hours computed across midnight).
```

**Definition of done**: three Appendix-B-based unit tests pass with the exact numbers from the study; multi-shift-day rollup produces one `AttendanceDaySummary` aggregating N `AttendanceRecord`s.

---

## Prompt 8 — Exception detection

```
Implement exception detection per the Business and Technical Study §9 and §14.1.

Create AttendanceException : Entity with fields exactly per study §9.3.

Extend IAttendanceProcessingEngine.ProcessEmployeeDayAsync (Prompt 7) to, after attempting
the pairing step, detect and raise an AttendanceException (ExceptionType +
MissingEventType set appropriately, Status=Open) for each of:

- Missing Check In (a CheckOut with no preceding CheckIn)
- Missing Check Out (a CheckIn with no following CheckOut, after a configurable grace
  window has elapsed — do not raise it the instant the shift ends, per study §9.2)
- Missing Break Out (a BreakIn with no preceding BreakOut, when the resolved shift's
  BreakTime=true)
- Missing Break In (a BreakOut with no following BreakIn, when BreakTime=true)
- Invalid sequence per the study §14.1 table: CheckOut before CheckIn, BreakIn without a
  prior BreakOut, two consecutive CheckIns (apply the duplicate-window check first per
  §14.1 — only raise InvalidSequence if the second CheckIn falls outside the duplicate
  window)

When any Open exception exists for an employee/day, the corresponding AttendanceRecord's
Status must be PendingReview, never auto-Approved — wire this gate explicitly (this is the
concrete enforcement point for CLAUDE.md invariant 5 and study §11.3).

A day with zero expected punches and zero actual punches must NOT raise an exception if
IScheduleResolutionService returned DayOff for that date — confirm this guard exists.

Write unit tests reproducing Appendix B Scenario 3 (missing Break Out) plus one test each
for: missing Check In, missing Check Out (both before and after the grace window), missing
Break In, CheckOut-before-CheckIn, and a DayOff date producing no exception at all.
```

**Definition of done**: all listed unit tests pass; an `AttendanceRecord` with any `Open` exception for that employee/day is never `Approved`.

---

## Prompt 9 — Corrections and approval workflow

```
Implement the correction workflow per the Business and Technical Study §11.

Create AttendanceCorrection : Entity with fields exactly per study §11.2.

Implement IAttendanceCorrectionService with:
- ProposeCorrectionAsync(exceptionId, proposedValue, justification, requestedByEmployeeId)
  -> creates an AttendanceCorrection with Status=Pending.
- DecideCorrectionAsync(correctionId, decidedByUserId, approved: bool, isHrDecision: bool)
  -> records ManagerDecision or HRDecision per AttendancePolicy.CorrectionApprovalWorkflow
  (Manager/HR/Both); only moves Status to Approved once all required approvals per policy
  are satisfied; on Approved, sets Status=Applied, triggers
  IAttendanceProcessingEngine.ProcessEmployeeDayAsync to recompute the AttendanceRecord with
  the corrected value folded in (new Version, per Prompt 7), and sets the originating
  AttendanceException.Status=Closed with ResolutionMethod=EmployeeCorrection/
  ManagerCorrection/HRCorrection as appropriate.
- On rejection: AttendanceCorrection.Status=Rejected, AttendanceException reverts to Open
  so a new correction can be proposed (study §11.1 state diagram).

Wire minimal API endpoints (match existing controller conventions from DISCOVERY_NOTES.md)
for: employee submits a correction against their own open exception; manager/HR lists
pending corrections for their scope and decides. Apply whatever RBAC/claims mechanism
DISCOVERY_NOTES.md found — do not invent a new auth scheme.

Write unit tests: propose -> manager-approve -> AttendanceRecord recalculated and exception
Closed (reuse the Prompt 8 missing-Break-Out scenario, matching study Appendix B Scenario
3's numbers exactly: BreakOut corrected to 12:00, final working hours recalculated);
propose -> manager-reject -> exception reopened; a policy requiring Manager+HR where only
Manager has decided stays Pending, not Approved.
```

**Definition of done**: Appendix B Scenario 3 reproduced end-to-end (exception raised in P8, corrected and approved here, final `AttendanceRecord` numbers match the study); dual-approval policy correctly withholds `Approved` until both decisions are in.

---

## Prompt 10 — Scheduled sync and reconciliation

```
Implement the synchronization strategy per the Business and Technical Study §12.4.

Extend AttendanceIntegration-driven ingestion with a scheduled job (using the background-job
mechanism confirmed in P0) that, per active AttendanceIntegration:

1. Reads SyncCursor/LastSuccessfulSyncAt.
2. Calls the provider adapter's GetTransactionChangesAsync with a small overlap window
   (configurable, default 5 minutes) subtracted from the stored cursor.
3. On success: advances SyncCursor/LastSuccessfulSyncAt only after the batch's raw rows are
   durably committed (per study §12.4 "only after a batch fully succeeds").
4. On failure: leaves the cursor untouched, applies Polly retry with exponential backoff +
   jitter, and opens a circuit breaker per integration after a configurable failure
   threshold, flipping AttendanceIntegration.HealthStatus to Unhealthy.

Add a separate nightly ReconciliationJob that re-pulls a wider window (default 48 hours)
than the incremental cursor and relies on the existing duplicate-detection machinery (Prompt
3/5) to absorb the overlap harmlessly.

Write tests: a successful sync advances the cursor; a failed sync (mock the provider to
throw) leaves the cursor unchanged and flips HealthStatus; a retried sync after failure
re-delivers overlapping data that lands as DuplicateConfirmed rather than double-processed
(verify no duplicate AttendanceRecord hour-contribution results).
```

**Definition of done**: three tests above pass; cursor-advance-only-on-success behavior is explicit and tested, not incidental.

---

## Prompt 11 — HR/Payroll extraction contract

```
Implement the payroll eligibility gate per the Business and Technical Study §18.3.

Add IPayrollAttendanceExtract.GetApprovedRecordsAsync(tenantScope, payPeriodStart,
payPeriodEnd) that returns AttendanceRecord/AttendanceDaySummary rows where ALL of:
- Status = Approved
- No AttendanceException referencing that employee/date remains Open or PendingApproval
- Any OvertimeMinutes > 0 where AttendancePolicy.RequiresOvertimePreApproval is true has an
  Approved AttendanceCorrection covering it
- AttendanceDate falls within [payPeriodStart, payPeriodEnd] and that period is not already
  marked closed (add a simple PayPeriod closed-flag concept if one doesn't already exist in
  the Payroll module — check DISCOVERY_NOTES.md/existing Payroll entities first and reuse
  if found).

This is the ONLY sanctioned read path into attendance data for anything outside this
feature — add an architecture test (if this codebase has an architecture-test project,
per DISCOVERY_NOTES.md) asserting that no project other than this feature's own references
RawAttendanceTransaction or NormalizedAttendanceTransaction directly.

Write unit tests: an Approved record with no open exceptions is returned; an Approved record
with an Open exception elsewhere for the same employee/date is excluded; an unapproved
overtime record is excluded until the pre-approval correction exists; a record in an
already-closed pay period is excluded.
```

**Definition of done**: four tests pass; architecture test (if applicable) passes and fails deliberately when you temporarily add a disallowed reference, confirming it actually catches the violation.

---

## Prompt 12 — Security hardening

```
Implement security measures per the Business and Technical Study §16.

1. Confirm AttendanceIntegration.ConnectionConfigRef (Prompt 1) actually resolves through
   this codebase's real secrets mechanism (Azure Key Vault, user-secrets, environment-based
   provider — whatever DISCOVERY_NOTES.md found or the team confirms) rather than the
   placeholder string from Prompt 1. Replace the placeholder with a real
   ISecretsAccessor-style abstraction if one exists, or propose one if it doesn't.

2. Add an audit log write (reuse an existing audit-log mechanism if DISCOVERY_NOTES.md found
   one; otherwise add a minimal AttendanceAuditLog : SystemEntity with Actor, Action,
   EntityType, EntityId, Timestamp, Details) at: every AttendanceCorrection decision, every
   AttendanceException status change, and every direct read of RawAttendanceTransaction or
   NormalizedAttendanceTransaction outside the normalization/processing engine itself (i.e.,
   ad hoc support/investigation queries).

3. Wire role checks (Employee/Manager/HR Administrator/Payroll Administrator/Integration
   Administrator, per study §16.5) onto every endpoint added in Prompts 4, 9, and 11, using
   whatever RBAC/claims mechanism DISCOVERY_NOTES.md confirmed.

Write tests confirming: a correction decision writes an audit entry with the correct actor;
an Employee-role user cannot call the manager/HR decision endpoint; a Payroll Administrator
can call GetApprovedRecordsAsync but not the correction-decision endpoint.
```

**Definition of done**: role-check tests pass (both allow and deny cases); audit entries are written and queryable for the three listed actions.

---

## Prompt 13 — Observability

```
Implement the KPIs and health signals per the Business and Technical Study §17.3, using
whatever metrics/logging infrastructure DISCOVERY_NOTES.md found (or a standard .NET
ILogger + a metrics library already present in the codebase — do not add a new metrics
package without confirming none already exists).

Emit/expose: synchronization success rate, failed transaction rate, duplicate rate, missing
event rate, average synchronization delay, and unresolved exception count — all scoped per
AttendanceIntegration and aggregatable platform-wide. Expose a simple
GetIntegrationHealthAsync(integrationId) query returning the composite health view described
in study §17.3's table, backing AttendanceIntegration.HealthStatus.

Write a test confirming GetIntegrationHealthAsync reflects a forced-Unhealthy state after
the Prompt 10 circuit-breaker scenario trips.
```

**Definition of done**: health query test passes; at minimum the six KPIs are computable from existing tables without new schema (confirm, and only add new columns/tables if truly unavoidable).

---

## Prompt 14 — Test suite consolidation

```
Review every unit/integration test added across Prompts 1-13 and consolidate them into a
clear AttendanceIntegrationTests test project structure (one fixture class per prompt/
concern). Add any missing worked-scenario coverage from the study's Appendix B that wasn't
already covered: Scenario 4 (duplicate from integration retry) end-to-end through ingestion
-> normalization -> processing, confirming the duplicate row never contributes hours.

Produce a short TEST_COVERAGE.md mapping each of the study's Appendix B scenarios and
§14.1/§9.2 decision-table rows to the specific test(s) that cover it, and flag any gap
honestly rather than claiming coverage that doesn't exist.
```

**Definition of done**: `TEST_COVERAGE.md` exists; every Appendix B scenario has at least one named test; any genuine gap is listed, not papered over.

---

## Prompt 15 — Seed data

```
Implement ISeedData for AttendanceProvider and AttendancePolicy per the seeding mechanism
DISCOVERY_NOTES.md confirmed (Prompt 0, question 3).

Seed AttendanceProvider with at minimum: GenericCsvProvider (matching Prompt 4),
GenericWebhookProvider (stub, SupportsWebhooks=true, for future use), and one or two
realistic named vendors from the study's examples (e.g., a ZKTeco-style biometric provider)
marked IsActive=false until a real adapter exists for them — do not seed a provider as
active unless Prompt 4's adapter (or a later one) actually implements it.

Seed one default tenant-wide AttendancePolicy (TimeScheduleId=null) with the MVP defaults
from study §20.1: UnlabeledPunchInterpretationMode=Off, RequiresOvertimePreApproval
configurable but default false, CorrectionApprovalWorkflow=Manager.
```

**Definition of done**: seed data appears after running whatever seeding command DISCOVERY_NOTES.md documented; no provider is marked active without a corresponding working adapter.
