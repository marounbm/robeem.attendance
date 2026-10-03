# robeem — Attendance & Time Clock Integration

This file is persistent project memory for implementing the **Attendance and Time Clock Machine Integration** capability inside the existing robeem codebase. Keep it at the repository root (or the root of the module/solution folder this feature lives in) so it loads automatically every session.

It is a **condensed, code-facing contract** distilled from the full 43-page design document, *"Attendance & Time Clock Machine Integration — Business and Technical Study"* (delivered 2026-09-07). That document is the source of truth for rationale, full schemas, every decision table, and edge-case handling — this file exists so day-to-day implementation sessions don't have to re-derive the architecture or re-discover the existing entity conventions from scratch. When this file and the study ever disagree, the study wins for *business rules*; this file wins for *how it maps onto the actual codebase*, since it was written after inspecting real entity source.

**Read this entire file before writing or modifying any Attendance-related code.**

---

## 0. The one rule that matters most

> **This feature is being added to an existing, populated codebase — not greenfield.** robeem's HR module already has a working shift/roster system (`TimeSchedule`, `TimeScheduleShift`, `TimeScheduleTeam`, `TimeScheduleLine`, `TimeScheduleEmployee`). Do **not** invent a parallel `WorkShift` / `EmployeeSchedule` entity. The design study's logical model must be *mapped onto* these existing entities (§3 below), not duplicated next to them.

If at any point an instruction here or in the study seems to call for a new entity that overlaps with something in §3, stop and re-read §3 — the existing entity almost certainly wins.

---

## 1. Architecture invariants (non-negotiable)

These are carried forward from the study's final recommendation (§20.4) and must hold regardless of which prompt/session implements which slice:

1. **Raw transactions are always stored, unconditionally, for every event from every provider.** Never gate raw insertion on validity or duplicate status.
2. **Raw data is immutable.** No code path may `UPDATE` or `DELETE` a raw transaction row after insert. Corrections happen downstream, never by editing raw or normalized rows.
3. **Normalization is asynchronous**, decoupled from ingestion by a queue/background job — never inline in the same request/transaction that received the data.
4. **Duplicate detection happens only at normalization**, via a composite business key (tenant + employee + device + event type + time bucket). Raw storage is duplicate-tolerant by design; the processing engine trusts the duplicate status set at normalization rather than re-deriving it.
5. **Missing punches are never invented.** A missing Check-In/Check-Out/Break-Out/Break-In always produces an exception requiring human resolution — never a fabricated timestamp.
6. **Automatic event *interpretation*** (assigning a type to an unlabeled-but-present punch) is allowed, only via an explicit, configurable policy, and the result is always marked as inferred, distinguishable from vendor-labeled data. Automatic event *invention* (filling in an absent punch) is never allowed.
7. **Manual corrections are additive, never destructive.** A correction is a new row referencing the thing it corrects, with actor identity, justification, and approval decision recorded. Raw/normalized data is never rewritten.
8. **Payroll reads only fully-approved, exception-free attendance records**, for a pay period not yet closed. Payroll code must never query raw or normalized attendance tables directly.
9. **New attendance vendors are additive configuration + one adapter.** They must never require a change to processing, exception, correction, or payroll code.
10. **No biometric templates are ever persisted.** Only the transaction (the fact that a match occurred) is stored. Every adapter mapping raw vendor payloads must allowlist fields, not pass the payload through unfiltered.

---

## 2. Existing codebase conventions (observed from `robeem.DataModel`)

Every new class in this feature must follow these exactly — they are not stylistic preferences, they are what makes the entity work with robeem's existing generic persistence/UI layer.

### 2.1 Base classes — which one to inherit

| Base class | Adds | Use for |
|---|---|---|
| `Entity` | `Id` (PK, int), `Title` (required, max 255, virtual — override when there's no natural display name), `CreatedOn/CreatedBy/ModifiedOn/ModifiedBy` (stored, virtual), `GUID` (stored), `PartitionName`, the `this[string key]` reflective indexer, `isSystemEntity()`/`isWorkflowEntity()` | Master/reference data and anything a human directly creates, views, or approves |
| `SystemEntity : Entity` | Same shape, but `CreatedOn`/`ModifiedOn`/`GUID`/`CreatedBy`/`ModifiedBy` are `[NotMapped]` overrides (computed or constant, **not persisted columns at all**) | High-volume, system-generated, non-audited rows — matches existing `TimeScheduleLine` and `TimeScheduleEmployee` |
| `TimeShiftEntity : Entity` | `StartTime`/`EndTime`/`BreakStartTime`/`BreakEndTime` as `"HH:mm"` strings (MaxLength 5), `Overtime` bool, `BreakTime` bool, plus NotMapped computed helpers (`WorkHours`, `BreakHours`, `TotalHours`, `Display*Time`) via the static `Set` helper | **Already used by `TimeScheduleShift`. Reuse this type for anything shift-shaped — do not create a parallel time-range entity.** |

Rule of thumb: if the table will have millions of rows and no single human ever "owns" a given row (raw punches, normalized events, processing batches), use `SystemEntity`. If it participates in an approval workflow or is something an HR admin configures (providers, devices, policies, exceptions, corrections, records), use `Entity`.

### 2.2 Table and schema declaration

```csharp
[Serializable, Table(nameof(ClassName), Schema = nameof(SmartEnum.Module.HR))]
public class ClassName : Entity, ISeedData
{
    ...
}
```

- Table name is always `nameof(TheClassItself)` — never a hand-typed string.
- Schema is always `nameof(SmartEnum.Module.X)`, where `SmartEnum` is `robeem.System.Contract.SmartEnum` (imported via `using SmartEnum = robeem.System.Contract.SmartEnum;`) and `Module` is an enum of bounded-context names.
- **Open question — resolve in Prompt 0**: does `SmartEnum.Module` already have (or can it gain) an `Attendance` member, or should every new entity in this feature live under `Module.HR` alongside `TimeSchedule*`? Default assumption until confirmed: **add `Module.Attendance`** — the raw/normalized tables are operationally a very different volume/retention profile than HR master data and deserve schema-level separation — but this must be verified against the real enum and any schema-per-module tooling before use.

### 2.3 `ISeedData`

Implemented today by `TimeSchedule`, `TimeScheduleShift`, `TimeScheduleTeam`, `TimeScheduleLine`, `TimeScheduleEmployee` — i.e., it is used broadly for HR config/reference-shaped data in this codebase, not just "pure" master data. Apply it to new **catalog/reference** entities (`AttendanceProvider`, `AttendancePolicy`) so they can be demo-seeded the same way. Do **not** apply it to high-volume transactional entities (`RawAttendanceTransaction`, `NormalizedAttendanceTransaction`, `AttendanceProcessingBatch`) — confirm this split against however `ISeedData` is actually consumed (Prompt 0).

### 2.4 Foreign keys and navigation properties

Convention observed on every existing entity:

```csharp
[Required]
public int XId { get; set; }
public X? X { get; set; }
```

The FK integer property is `[Required]`; the navigation property is nullable reference, named **exactly** the FK name minus `Id`. This matters beyond style: `Entity.this[string key]` resolves `key` to either a scalar property or, when `key` resolves to an `Entity`-typed navigation property, rewrites the access to `{key}Id` — i.e., **robeem's generic reflective layer depends on this exact naming pairing.** Breaking it (e.g., a nav property not named `{Fk}Id` minus `Id`) will silently break whatever generic grid/form/API machinery walks entities by property name.

**Naming follows role, not type.** The employee-reference pattern in `TimeScheduleEmployee` is:

```csharp
[Required]
public int EmployeeId { get; set; }
public Profile? Employee { get; set; }
```

Note the type is `Profile` (robeem's actual person/user entity), but the property is named `Employee` for the role it plays. **Every new Attendance entity referencing "the employee" must follow this exact pattern** (`EmployeeId` / `Employee` of type `Profile`) — do not invent an `Employee` entity type, and do not name the property `ProfileId`/`Profile`.

### 2.5 `Title` override for entities with no natural display name

```csharp
[NotMapped]
public override string Title => PartitionName;
```

Seen on `TimeScheduleLine` and `TimeScheduleEmployee` — both are composite/junction-shaped rows with no single meaningful display string. Apply the same pattern to `RawAttendanceTransaction`, `NormalizedAttendanceTransaction`, and `AttendanceProcessingBatch`. For entities that *do* have a natural label (`AttendanceProvider.DisplayName`, `AttendanceException`, `AttendanceCorrection`), compose a readable `Title` instead (e.g., `$"{ExceptionType} — {AttendanceDate:d}"`).

### 2.6 Computed/derived properties

Use `#region not mapped` for `[NotMapped]` computed properties and `#region method` for behavior methods — both regions appear in `TimeSchedule`, `TimeShiftEntity`, `TimeScheduleLine`. Prefer a computed `[NotMapped]` property over a stored column whenever the value is derivable (see `TimeShiftEntity.WorkHours`, `TimeSchedule.CycleStartDay`). This directly affects one specific decision below: **don't add a stored `IsOvernight` column** — derive it.

### 2.7 Static helpers

`Set` (namespace not shown, presumably `robeem.System` or similar) provides `Set.asTime(string)`, `Set.TimeSpan(string, string[, string, string])`, `Set.isSystemEntity(Type)`, `Set.isWorkflowEntity(Type)`. Reuse `Set.asTime`/`Set.TimeSpan` for any `"HH:mm"`-string time arithmetic in the processing engine — do not reimplement time-string parsing.

### 2.8 Multi-tenancy — **unresolved, verify before writing any migration**

**None of the supplied entities carry a `TenantId` column or any visible tenant-scoping base class.** The design study (§15) recommended a shared database with a `TenantId` column on every table, by analogy with robeem's storage service. That analogy does **not** carry over automatically — this codebase may instead use a database-per-tenant or schema-per-tenant deployment model, a connection-string-per-tenant resolver, or a global EF Core query filter applied somewhere not visible in these five files.

**Do not add a `TenantId` column to any new entity until Prompt 0 has confirmed the actual mechanism.** Getting this wrong is the single highest-risk mistake available in this feature — it is a cross-tenant data leak, not a cosmetic error.

---

## 3. Mapping the study's logical model onto existing entities

The study (§13) proposed `WorkShift`, `EmployeeSchedule`, and `AttendancePolicy` as new entities. Only `AttendancePolicy` is actually new. The rest already exist, in a richer form (a rotating-cycle roster rather than a flat weekly pattern):

| Study concept (§13.2) | Real entity/mechanism | Notes |
|---|---|---|
| `WorkShift` (start/end, break rules, overtime flag) | **`TimeScheduleShift`** (via `TimeShiftEntity`) | Already has `StartTime`, `EndTime`, `BreakTime`, `BreakStartTime`, `BreakEndTime`, `Overtime`. Scoped to a `TimeScheduleId`. |
| `EmployeeSchedule` (which shift an employee works, and when) | **`TimeScheduleEmployee` + `TimeScheduleTeam` + `TimeSchedule.GetCycleDay(date)` + `TimeScheduleLine`** | See resolution algorithm below. There is no single table to query — it's a 3-table join plus one computed method call. |
| Overnight shift flag | *(derive, don't store)* | `TimeShiftEntity` has no `IsOvernight` column. Add a `[NotMapped]` computed property (e.g., on a partial/extension of `TimeScheduleShift`, or a small wrapper DTO) that returns `true` when `EndTime` (as time-of-day) is earlier than `StartTime`. Confirm exact placement with the team/Prompt 0 — do not add a stored column. |
| `AttendancePolicy` (rounding, grace periods, OT threshold, correction workflow, interpretation mode) | **Genuinely new — does not exist today.** | Recommend scoping it per `TimeScheduleId` (one policy per schedule, with a tenant-wide default), rather than per-employee. Confirm this scoping decision in Prompt 0/1 — `TimeScheduleTeam.AllInclusive` hints at a "default bucket" pattern worth mirroring. |

### 3.1 Resolving "what shift should employee E work on date D" (new domain service, not a new table)

This logic doesn't exist yet as a single queryable thing — it must be implemented as a service method, e.g. `IScheduleResolutionService.ResolveShift(int employeeId, DateTime date)`:

1. Look up `TimeScheduleEmployee` for `employeeId` → get `TimeScheduleId` + `TeamId`.
2. Load the `TimeSchedule` row for that `TimeScheduleId`; call `timeSchedule.GetCycleDay(date)` → get the cycle day number (1..`CyclePeriod`).
3. Look up `TimeScheduleLine` where `TimeScheduleId` + `TeamId` + `CycleDay` match → get `ShiftId` (a line may not exist for a given team/cycle-day combination — that almost certainly means "scheduled day off"; confirm this interpretation in Prompt 0 rather than assuming).
4. Load the `TimeScheduleShift` for that `ShiftId` → this is the expected shift (start/end/break/overtime) the Attendance Processing Engine compares actual punches against.

**This resolution result is exactly the input the study calls `EmployeeSchedule`/`WorkShift` throughout §8, §9, and §14.** Every place the study says "compare against the employee's expected schedule," read it as "call `IScheduleResolutionService.ResolveShift`."

### 3.2 `TimeScheduleTeam.AllInclusive` — unresolved

Its purpose isn't fully determinable from the given source (no join/filter logic shown that reads it). It's plausibly "the default team an employee falls into if not explicitly assigned elsewhere." **Do not assume this in the resolution service without confirming** — an employee with no `TimeScheduleEmployee` row should probably raise a clear "no schedule configured" condition rather than guessing which `AllInclusive` team applies, unless Prompt 0 finds existing logic that already does this.

---

## 4. New entities this feature introduces

All in `robeem.DataModel`, following §2 conventions exactly. Base class and `ISeedData` recommendations below — confirm the schema module name per §2.2 before the first migration.

| Entity | Base class | `ISeedData`? | Purpose (study ref) |
|---|---|---|---|
| `AttendanceProvider` | `Entity` | Yes | Vendor catalog (§5.3, §12.1) |
| `AttendanceIntegration` | `Entity` | No | Tenant's configured connection to a provider (§12.1) — carries the high-water mark |
| `AttendanceDevice` | `Entity` | No | Physical/logical device mapped to a tenant (§13.2) |
| `AttendancePolicy` | `Entity` | Yes | Rounding/grace/OT/interpretation/correction-workflow config (§13.2, §14.2) |
| `AttendanceProcessingBatch` | `SystemEntity` | No | One ingestion/normalization/processing/reprocessing run (§6.3, §12.4) |
| `RawAttendanceTransaction` | `SystemEntity` | No | Immutable vendor-exact copy of every punch (§6.2) |
| `NormalizedAttendanceTransaction` | `SystemEntity` | No | Vendor-neutral interpreted event (§7.3) |
| `AttendanceException` | `Entity` | No | Detected data-quality/policy issue (§9.3) |
| `AttendanceCorrection` | `Entity` | No | Proposed/approved fix to an exception (§11.2) |
| `AttendanceRecord` | `Entity` | No | Computed per-shift result (§13.2) |
| `AttendanceDaySummary` | `Entity` | No | Per-employee/day rollup of one or more records (§13.2) |

Full field lists for every entity are in the study, §6.2, §7.3, §9.3, §11.2, §13.2 — do not re-derive them from scratch; the prompt series below references the exact section for each.

---

## 5. Commands

**Placeholders below — confirm the real values against the actual solution/project structure in Prompt 0 before relying on them.** Do not assume a solution file name, project name, or test runner that hasn't been confirmed against the real repo.

```bash
# Build
dotnet build <Solution>.sln

# Run the API host (name TBD — confirm in Prompt 0)
dotnet run --project src/<ApiProjectName>

# EF Core migrations (confirm DbContext name and migrations project in Prompt 0)
dotnet ef migrations add <MigrationName> --project src/<DataProject> --startup-project src/<ApiProjectName>
dotnet ef database update --project src/<DataProject> --startup-project src/<ApiProjectName>

# Tests
dotnet test
```

---

## 6. Implementation roadmap

Checklist form of the full prompt series (see `PROMPT_PLAN.md` for the actual prompts to run, in order):

- [ ] **P0** — Investigate: multi-tenancy mechanism, `SmartEnum.Module` extensibility, `ISeedData` consumption, generic UI/API conventions over `Entity`, existing background-job infrastructure (Hangfire or similar), existing DbContext/repository pattern.
- [ ] **P1** — Catalog/config entities: `AttendanceProvider`, `AttendanceIntegration`, `AttendanceDevice`, `AttendancePolicy` + EF configuration + migration.
- [ ] **P2** — `RawAttendanceTransaction` + `AttendanceProcessingBatch` + immutability enforcement.
- [ ] **P3** — `NormalizedAttendanceTransaction` + composite-key unique index.
- [ ] **P4** — `IAttendanceProvider` abstraction + `GenericCsvProvider` + ingestion service (manual upload path first).
- [ ] **P5** — Normalization engine (raw → normalized, employee/device resolution, duplicate detection).
- [ ] **P6** — `IScheduleResolutionService` (wraps `TimeSchedule`/`TimeScheduleLine`/`TimeScheduleTeam`/`TimeScheduleEmployee`).
- [ ] **P7** — Attendance Processing Engine (`AttendanceRecord`, `AttendanceDaySummary`) using P6 + `AttendancePolicy`.
- [ ] **P8** — Exception detection (`AttendanceException`): missing punches, invalid sequences.
- [ ] **P9** — `AttendanceCorrection` + approval workflow + ESS/manager endpoints.
- [ ] **P10** — Scheduled polling ingestion job, incremental sync via high-water mark, reconciliation job.
- [ ] **P11** — HR/Payroll extraction contract (Approved-only).
- [ ] **P12** — Security hardening (secrets reference, audit logging, RBAC).
- [ ] **P13** — Observability (KPIs, integration health, alerts).
- [ ] **P14** — Tests (Appendix B worked scenarios as automated cases).
- [ ] **P15** — Seed data for `AttendanceProvider` catalog.

---

## 7. Coding style and static analysis (StyleCop.Analyzers)

**Every `.csproj` this feature touches or creates — whether that's the existing `robeem.DataModel` project or any new project this feature introduces (e.g. the Application/Infrastructure split hinted at in Prompt 4) — must build clean under StyleCop.Analyzers.** This is a hard requirement, not a nice-to-have: it was added explicitly because `robeem.attendance` starts with no `.csproj` at all, so StyleCop must be wired in from the very first project file this feature creates, rather than retrofitted later.

### 7.1 Package reference

There is no stable 1.x release of StyleCop.Analyzers as of this writing — pin the current prerelease and treat upgrading it as a deliberate, separate decision, never an incidental `dotnet add package` bump:

```xml
<ItemGroup>
  <PackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.556">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
</ItemGroup>
```

Add this `ItemGroup` to **every** `.csproj` this feature creates. If this feature's code ends up living inside the existing `robeem.DataModel.csproj` (the common case for the new entities in §4), add the same reference there too — do not leave the existing project unanalyzed just because it predates this feature.

**Before adding it, check in Prompt 0 whether the main robeem solution already references StyleCop.Analyzers somewhere else (a `Directory.Build.props`, a shared `.editorconfig` at the solution root, or another project).** If it does, reuse that existing configuration and version instead of introducing a second, possibly conflicting one — two `.editorconfig`/`stylecop.json` files with different severities fighting over the same files is worse than having none. Only apply everything in this section if Prompt 0 confirms no such configuration already exists.

### 7.2 `.editorconfig` — per-rule severity, not a blanket switch

Place an `.editorconfig` at the repository root of `robeem.attendance` (next to this file) — Roslyn picks it up automatically for any project built underneath it, so no per-project wiring is needed beyond the package reference itself. **Do not set `TreatWarningsAsErrors` for the whole project.** StyleCop rules are enforced individually via `dotnet_diagnostic.SAxxxx.severity`; a rule set to `error` already fails the build on its own, without turning unrelated/pre-existing warnings (nullable, obsolete API, etc.) into build breaks as a side effect.

Severities are tiered deliberately:

| Tier | Severity | Rationale |
|---|---|---|
| Naming & readability (SA1300s, SA13xx member-naming, SA1500s braces) | `error` | These catch real inconsistency with §2's conventions (e.g. the `XId`/`X` navigation pairing) and should block the build. |
| Ordering, spacing, blank-line layout (SA1200s usings, SA1500–1518 layout, SA1000–1028 spacing) | `warning` | Worth fixing, not worth blocking a build over — `dotnet format` (see below) auto-fixes nearly all of these. |
| Documentation (SA16xx) and file headers (SA1633) | `none` | None of the existing `robeem.DataModel` files (`Entity`, `SystemEntity`, `TimeSchedule*`) carry XML doc comments or file headers — matching that existing style rather than forcing it retroactively. Revisit if the team later adopts doc-comment requirements platform-wide. |

The full `.editorconfig` content is provided as a separate file alongside this one (`.editorconfig`) — copy it into the `robeem.attendance` repo root as-is and adjust only with a documented reason.

**One rule needs a human decision, don't guess it**: SA1309 (`FieldNamesMustNotBeginWithUnderscore`) depends on whether this codebase's private-field convention is `camelCase` or `_camelCase`. The supplied `.editorconfig` ships with SA1309 at `warning` rather than `error` until Prompt 0 confirms the real convention from the broader robeem codebase (it isn't visible in the five supplied entity files, which have no private fields) — then promote it to `error` once confirmed.

### 7.3 `stylecop.json`

StyleCop.Analyzers also reads a `stylecop.json` settings file (company name for header rules — unused here since SA1633 is `none`, but still required for the analyzer to initialize cleanly; culture for documentation rules). Provided as a separate file (`stylecop.json`) — copy it into the `robeem.attendance` repo root and reference it from every `.csproj` that uses the analyzer:

```xml
<ItemGroup>
  <AdditionalFiles Include="$(MSBuildThisFileDirectory)..\stylecop.json" Link="stylecop.json" />
</ItemGroup>
```

(Adjust the relative path per the actual project's depth under the repo root once the real folder layout exists.)

### 7.4 Vendored/pre-existing files — path-scoped relaxation, never a blanket exclusion

If this feature's new code ends up compiled into the **same project** as the existing, already-shipped `robeem.DataModel` files (`Entity.cs`, `SystemEntity.cs`, `TimeShiftEntity.cs`, `TimeSchedule.cs`, `TimeScheduleShift.cs`, `TimeScheduleTeam.cs`, `TimeScheduleLine.cs`, `TimeScheduleEmployee.cs`, and anything else already in that folder), enabling StyleCop on the project enables it for those files too. **Do not reformat or rewrite those pre-existing files just to satisfy the analyzer** — add a path-scoped `.editorconfig` override instead, so enforcement stays strict for new code without forcing a noisy, risk-free-but-unnecessary diff across files this feature didn't write:

```ini
# Pre-existing robeem.DataModel files: relax layout/ordering rules that would otherwise
# force cosmetic-only changes to files this feature did not write. Naming-convention
# rules (SA1300-series) are intentionally NOT relaxed here — the existing files already
# follow §2's conventions, so they should pass those unmodified.
[**/DataModel/{Entity,SystemEntity,TimeShiftEntity,TimeSchedule,TimeScheduleShift,TimeScheduleTeam,TimeScheduleLine,TimeScheduleEmployee}.cs]
dotnet_diagnostic.SA1101.severity = none   # Prefix local calls with this.
dotnet_diagnostic.SA1201.severity = none   # Elements must appear in the correct order
dotnet_diagnostic.SA1516.severity = none   # Elements must be separated by blank line
```

Adjust the glob to the real relative path once Prompt 0 confirms where `robeem.DataModel` actually lives relative to `robeem.attendance`'s repo root (they may not be in the same repo at all, in which case this override is moot and can be deleted).

### 7.5 Enforcement in practice

- Run `dotnet format --verify-no-changes` (or `dotnet format` to auto-fix) before considering any prompt in `PROMPT_PLAN.md` done — this fixes essentially all `warning`-tier layout/spacing findings automatically, matching the practice already established in the sibling `robeem.storage` project.
- A clean `dotnet build` with zero StyleCop errors is part of the Definition of Done for **every** prompt from Prompt 1 onward (see the updated Prompt 1 in `PROMPT_PLAN.md`), not just a final cleanup pass.
- Do not suppress a StyleCop error with an inline `#pragma warning disable` to make a single file pass — fix the code, or if the rule is genuinely wrong for this codebase, change its severity in `.editorconfig` with a comment explaining why, so the decision is visible and consistent project-wide.

---

## 8. What *not* to do

- Do not create `WorkShift` or `EmployeeSchedule` entities — see §3.
- Do not add a `TenantId` column to anything before P0 confirms the real multi-tenancy mechanism.
- Do not let any code path `UPDATE` or `DELETE` a `RawAttendanceTransaction` row.
- Do not let the Payroll extraction query touch `RawAttendanceTransaction` or `NormalizedAttendanceTransaction`.
- Do not store a biometric template, under any field name, in `RawAttendanceTransaction.RawPayload` or anywhere else — allowlist fields at the adapter boundary.
- Do not invent a `dotnet`/`dotnet ef` command path that hasn't been confirmed against the real repo structure.
- Do not set `TreatWarningsAsErrors` on any project as a substitute for the tiered `.editorconfig` severities in §7 — it turns unrelated existing warnings into build breaks and is not how enforcement here is designed to work.
- Do not reformat a pre-existing `robeem.DataModel` file to satisfy StyleCop — use the path-scoped override in §7.4 instead.
