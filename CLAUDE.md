# robeem — Attendance & Time Clock Integration

This file is persistent project memory for implementing the **Attendance and Time Clock Machine Integration** capability inside the existing robeem codebase. Keep it at the repository root (or the root of the module/solution folder this feature lives in) so it loads automatically every session.

It is a **condensed, code-facing contract** distilled from the full 43-page design document, *"Attendance & Time Clock Machine Integration — Business and Technical Study"* (delivered 2026-09-07). That document is the source of truth for rationale, full schemas, every decision table, and edge-case handling — this file exists so day-to-day implementation sessions don't have to re-derive the architecture or re-discover the existing entity conventions from scratch. When this file and the study ever disagree, the study wins for *business rules*; this file wins for *how it maps onto the actual codebase*, since it was written after inspecting real entity source.

**Read this entire file before writing or modifying any Attendance-related code.**

> **Standalone solution (decided 2026-10-04).** The host robeem codebase is not available to this project: the developer is external and received only the 8 shared entity files. This repository is therefore a **complete, standalone solution**. It contains the shared entities (moved to `src/robeem.Domain`, namespaces unchanged) and every type they depend on (`SmartEnum`, `ISeedData`, `Set`, `Profile`, ...). It also has its own persistence, tenancy, HR screens and tests. The Attendance feature is built on top of it. See §9 for the solution layout.

> **Layered structure (decided 2026-10-04).** The solution is split into **Domain, Contracts, Application, Infrastructure, Api and Web** projects (§9). `robeem.Api` is the only process that touches a database. `robeem.Web` is an MVC front end that reaches data only through `robeem.Api` over HTTP.

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
- **Decided:** `SmartEnum` is defined in this solution (`src/robeem.Domain/Core/SmartEnum.cs`, namespace `robeem.System.Contract`) as a static class with a plain nested `enum Module { HR, Attendance }`. Adding a module means adding a member; the schema follows from the name. **HR entities (`Profile`, `TimeSchedule*`) use `Module.HR`. All new Attendance entities use `Module.Attendance`**, so the high-volume raw/normalized tables sit in their own schema.
- In new files, write the two attributes separately (`[Serializable]` then `[Table(...)]`) and put the `using SmartEnum = ...;` alias after the `System` usings. This keeps StyleCop's SA1133/SA1208/SA1209 quiet; only the supplied files are exempt from those rules (§7.4).

### 2.3 `ISeedData`

Implemented by `TimeSchedule`, `TimeScheduleShift`, `TimeScheduleTeam`, `TimeScheduleLine`, `TimeScheduleEmployee`. Apply it to new **catalog/reference** entities (`AttendanceProvider`, `AttendancePolicy`). Do **not** apply it to transactional entities (`RawAttendanceTransaction`, `NormalizedAttendanceTransaction`, `AttendanceProcessingBatch`) or to `Profile`.

**How it is consumed (this solution):** `ISeedData` is an empty marker interface (`src/robeem.Domain/Core/ISeedData.cs`).

- Seed data comes from `ISeedContributor` implementations (`src/robeem.Application/Seeding`) registered in DI. A contributor receives an `IRobeemDbContext`. An example is `HrSeedContributor` (`src/robeem.Application/HR/Seeding`), which seeds a "Standard Week" schedule (Mon–Fri 09:00–17:00) with its all-inclusive team.
- `SeedDataRunner` runs them for each tenant database every time `TenantDatabaseInitializer` runs: at startup when `Database:InitializeOnStartup` is true, which is the Development setting.
- **Contributors must be idempotent**: only add what is missing.
- The runner **rejects** any contributor that stages a change to an entity type not marked `ISeedData`, and saves nothing from it.
- Implementing the marker on a new type is always safe: it only *permits* seeding, and does nothing on its own.

To seed the provider catalog (P15), add an `ISeedContributor`.

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

`Set` lives in `src/robeem.Domain/Core/Set.cs`, namespace `robeem.DataModel`. It was written here because the host's version wasn't supplied, and its member names are dictated by the shared files.

- `Set.asTime(string)` → a `DateTime` carrying that time of day.
- `Set.TimeSpan(start, end)` → hours. **An end earlier than the start counts into the next day**, so `22:00`→`06:00` is 8 hours (overnight shifts).
- `Set.TimeSpan(start, end, breakStart, breakEnd)` → work hours minus break hours.
- `Set.ParseTime` / `Set.IsValidTime` → strict `H:mm`/`HH:mm` parsing, 00:00–23:59.
- `Set.isSystemEntity(Type)` → the type derives from `SystemEntity`.
- `Set.isWorkflowEntity(Type)` → the type implements the `IWorkflowEntity` marker. **Put that marker on `AttendanceCorrection` (P9).**

Reuse these for all `"HH:mm"` arithmetic in the processing engine. Don't reimplement time parsing.

The indexer helpers `getPropertyValue`/`setPropertyValue` are in `EntityPropertyExtensions.cs`:

- An unknown or read-only property throws.
- Values are converted to the property type.
- **Caveat:** `entity["Team"] = 5` writes `TeamId` only when the `Team` navigation is already loaded (that is how the shared `Entity` indexer is written). Otherwise, set `TeamId` directly.

**Namespace gotcha:** the namespace `robeem.System` exists. Inside any `namespace robeem.*`, a qualified `System.Foo` resolves to `robeem.System.Foo` and fails. Rely on the `using` directives, or write `global::System.Foo`.

### 2.8 Multi-tenancy — **decided: database per tenant, plus `TenantId` on every entity**

Decided by the product owner (2026-10-04): **every tenant has its own database**, and **tenancy is also carried at entity level through a `TenantId`**. How it is implemented (paths relative to `src/robeem.Infrastructure` unless stated otherwise):

| Piece | Where | What it does |
|---|---|---|
| Abstractions | `src/robeem.Application/Tenancy`, `src/robeem.Application/Persistence` | `ITenantContext`, `ITenantRegistry`, `ITenantScopeFactory`, `TenantDefinition`, the tenancy exceptions, and `IRobeemDbContext`. **Application services depend only on these**, never on Infrastructure types. |
| Tenant registry | `Tenancy/TenantRegistry.cs`, config section `Tenancy:Tenants` (in `robeem.Api`'s settings) | `Id` (lower-case, max 64), `Name`, `ConnectionString` per tenant. Validated at startup; bad config fails fast. |
| Tenant context | `Tenancy/TenantContext.cs` (scoped) | Set **once** per scope and can't be switched within it. HTTP requests set it in the Api's `TenantResolutionMiddleware`; anything else uses `ITenantScopeFactory.CreateScope(tenantId)`. |
| Database selection | `InfrastructureServiceCollectionExtensions.AddRobeemInfrastructure` | `RobeemDbContext` connects to the scope's tenant database. Resolving it with no tenant throws `TenantNotResolvedException` (fails closed). |
| `TenantId` column | `Persistence/RobeemDbContext.OnModelCreating` | **Every table gets a required `TenantId varchar(64)`.** Entities implementing `ITenantScoped` expose it as a CLR property. All others, including the supplied `TimeSchedule*` files, get it as an EF shadow property, so the supplied files stay unmodified. |
| Write guard | `RobeemDbContext.SaveChanges*` | Stamps `TenantId` on insert. Inserting for another tenant, or modifying/deleting another tenant's row, throws `TenantIsolationException`. |
| Read guard | global query filter on every entity | Returns only rows whose `TenantId` equals the current tenant. |

**Rules for new code:**

- **Every new entity implements `ITenantScoped`** (a `string TenantId` property with `[Required][MaxLength(ITenantScoped.TenantIdMaxLength)]`), as `Profile` does. Don't set it by hand; the DbContext stamps it.
- Composite unique indexes start with `TenantId`. For example, the normalization duplicate key (§1.4) is `(TenantId, EmployeeId, DeviceId, EventType, TimeBucket)`.
- Background jobs (P5, P10) iterate `ITenantRegistry.Tenants` and run each tenant's work inside `ITenantScopeFactory.CreateScope(tenant.Id)`. Never share one DbContext across tenants.
- Never call `IgnoreQueryFilters()` outside tests.
- Updating a detached `SystemEntity` row (shadow `TenantId`) via `Update`/`Attach` throws, because the original `TenantId` is unknown. Load the row first, then change it.

**Database engine: PostgreSQL** (decided 2026-10-04), through `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3. Each tenant is a separate PostgreSQL database; `MigrateAsync` creates it on first start. Rules that follow from the engine:

- **`DateTime` columns are `timestamp without time zone`.** `RobeemDbContext` maps every `DateTime`/`DateTime?` this way, because robeem stores local wall-clock times (the shared entities default to `DateTime.Now`). Npgsql's default column type, `timestamp with time zone`, only accepts UTC values. New entities get this mapping automatically. Don't override it per property, and don't write `DateTimeKind.Utc` values to these columns: Npgsql rejects them. (`utc.UtcDateTime` and its `.Date` are UTC-kind; wrap them in `DateTime.SpecifyKind(..., DateTimeKind.Unspecified)`.) SQLite accepts such values, so `TestDatabase` adds `UtcDateTimeGuardInterceptor`, which makes every test save fail the way PostgreSQL would.
- **`DateTimeOffset` columns are `timestamp with time zone`, stored in UTC.** Use `DateTimeOffset` for instants such as punch and receipt times. Npgsql rejects a non-zero offset, so `RobeemDbContext` converts every `DateTimeOffset` to UTC on write, on every provider. Values read back always have offset zero; keep the vendor's original text, such as `RawAttendanceTransaction.TransactionTimestampRaw`, when the offset matters. A *local* time belongs in a `DateTime` plus an IANA `TimeZoneId`, never in a `DateTimeOffset` (see `NormalizedAttendanceTransaction.EventTimestampLocal`).
- **Text comparison is case-sensitive.** Unique indexes are case-sensitive, so `E-1` and `e-1` are different employee numbers. User-facing search must lower-case both sides (`x.ToLower().Contains(term)`, as `EmployeeService.ListAsync` does). Don't use `EF.Functions.ILike`: it is PostgreSQL-only, and the tests run on SQLite.
- Provider-specific model configuration goes behind `if (Database.IsNpgsql())` in `OnModelCreating`, so the SQLite test model still builds. robeem.storage follows the same pattern.
- Raw/JSON payload columns (P2) use `jsonb` or `text`, not SQL Server's `NVARCHAR(MAX)` that `PROMPT_PLAN.md` mentions.

**Request tenant resolution** happens in the Api (`src/robeem.Api/Tenancy/TenantResolutionMiddleware.cs`):

- The authenticated user's `tenant_id` claim comes first.
- Only when `Tenancy:AllowUnauthenticatedTenantSelection` is true (**Development only**) is the tenant taken from, in order:
  1. the `X-Tenant-Id` header (`TenancyProtocol.TenantHeader` in `robeem.Contracts`);
  2. `Tenancy:DefaultTenantId`.
- A rejection is a ProblemDetails response with a `code` extension: `tenant_required` (403), `unknown_tenant` (400) or `tenant_unavailable` (403). The constants are in `TenancyProtocol`.
- Controllers or actions that must work without a tenant carry `[AllowWithoutTenant]`, as `GET api/tenants` does.
- **There is no authentication in this solution yet.** In production, `AllowUnauthenticatedTenantSelection` stays false, so no request reaches tenant data until authentication issues a `tenant_id` claim (P12).

**On the Web side** (`src/robeem.Web/Tenancy`):

- The `/Tenants` picker stores the chosen tenant in the `robeem.tenant` cookie.
- `TenantHeaderHandler` forwards that cookie to the Api as `X-Tenant-Id`, but only when it is shaped like a tenant id.
- When the Api rejects the tenant, `TenantSelectionRequiredFilter` redirects GET requests to the picker. If the tenant was unknown, it deletes the cookie first.
- The Web has no tenant configuration of its own. The tenant list and the current tenant come from `GET api/tenants` and `GET api/tenants/current`.

---

## 3. Mapping the study's logical model onto existing entities

The study (§13) proposed `WorkShift`, `EmployeeSchedule`, and `AttendancePolicy` as new entities. Only `AttendancePolicy` is actually new. The rest already exist, in a richer form (a rotating-cycle roster rather than a flat weekly pattern):

| Study concept (§13.2) | Real entity/mechanism | Notes |
|---|---|---|
| `WorkShift` (start/end, break rules, overtime flag) | **`TimeScheduleShift`** (via `TimeShiftEntity`) | Already has `StartTime`, `EndTime`, `BreakTime`, `BreakStartTime`, `BreakEndTime`, `Overtime`. Scoped to a `TimeScheduleId`. |
| `EmployeeSchedule` (which shift an employee works, and when) | **`TimeScheduleEmployee` + `TimeScheduleTeam` + `TimeSchedule.GetCycleDay(date)` + `TimeScheduleLine`** | See resolution algorithm below. There is no single table to query — it's a 3-table join plus one computed method call. |
| Overnight shift flag | *(derive, don't store)* | `TimeShiftEntity` has no `IsOvernight` column. The supplied `TimeScheduleShift` is not `partial` and must not be edited (§7.4), so add an **extension method** `IsOvernight(this TimeShiftEntity shift)` in `src/robeem.Application/Attendance`. It returns `Set.ParseTime(shift.EndTime) < Set.ParseTime(shift.StartTime)`. `Set.TimeSpan` already counts such shifts into the next day. Do not add a stored column. |
| `AttendancePolicy` (rounding, grace periods, OT threshold, correction workflow, interpretation mode) | **Genuinely new — does not exist today.** | Scope it per `TimeScheduleId`: one policy per schedule, plus one tenant-wide default with `TimeScheduleId = null`, rather than per employee. This mirrors the default-team pattern of `TimeScheduleTeam.AllInclusive` (§3.2). |

### 3.1 Resolving "what shift should employee E work on date D" (new domain service, not a new table)

This logic doesn't exist yet as a single queryable thing — it must be implemented as a service method, e.g. `IScheduleResolutionService.ResolveShift(int employeeId, DateTime date)`:

1. Look up `TimeScheduleEmployee` for `employeeId` → get `TimeScheduleId` + `TeamId`.
2. Load the `TimeSchedule` row for that `TimeScheduleId`; call `timeSchedule.GetCycleDay(date)` → get the cycle day number (1..`CyclePeriod`).
3. Look up `TimeScheduleLine` where `TimeScheduleId` + `TeamId` + `CycleDay` match → get `ShiftId`. No line for a team/cycle-day combination means a scheduled day off; see §3.2 for which teams' lines apply.
4. Load the `TimeScheduleShift` for that `ShiftId` → this is the expected shift (start/end/break/overtime) the Attendance Processing Engine compares actual punches against.

**Implemented (P6)** as `IScheduleResolutionService` in `src/robeem.Application/Attendance`:

- `ResolveShiftAsync(employeeId, date)` and `ResolveShiftsInRangeAsync(employeeId, from, to)`. The range method loads the roster once; use it for multi-day work.
- Both return a `ShiftResolution` per date: `Scheduled`, `DayOff` or `NoScheduleConfigured`.
- A `Scheduled` day carries a **list** of `ResolvedShift`, ordered by start time. A team can have several lines on one cycle day (a split shift), so never assume exactly one.
- Each `ResolvedShift` has `StartsAt`/`EndsAt` as local wall-clock times. `EndsAt` falls on the next day for an overnight shift.

**This resolution result is exactly the input the study calls `EmployeeSchedule`/`WorkShift` throughout §8, §9, and §14.** Every place the study says "compare against the employee's expected schedule," read it as "call `IScheduleResolutionService.ResolveShift`."

### 3.2 `TimeScheduleTeam.AllInclusive` — decided

**Definition (product owner, 2026-10-04):** an all-inclusive team includes *all employees assigned to the time schedule*. It is the **default team of a schedule**.

What this means in code:

- **Assignment** (implemented in `src/robeem.Application/HR/Employees/EmployeeService.cs`):
  - Assigning an employee to a schedule creates or updates their single `TimeScheduleEmployee` row with `TeamId` = that schedule's all-inclusive team. If there are several, it takes the lowest `Id`.
  - A schedule with no all-inclusive team can't be assigned; the user gets a validation error.
  - An employee has **at most one** schedule assignment, enforced by a unique index on `(TenantId, EmployeeId)`.
  - Moving an employee to another schedule moves them to that schedule's default team. Staying on the same schedule keeps their current team.
- **Resolution** (P6, `IScheduleResolutionService`):
  - An employee with **no** `TimeScheduleEmployee` row is not on any schedule. Return an explicit "no schedule configured" result. Never guess a schedule.
  - For an assigned employee, `TimeScheduleLine` rows of the schedule's all-inclusive team apply to them, because that team contains everyone on the schedule. So do lines of the team they are explicitly in.
  - No line for a cycle day means a scheduled day off. Example: the seeded "Standard Week" has lines only for cycle days 1–5 (Mon–Fri).

---

## 4. New entities this feature introduces

All in **namespace** `robeem.DataModel`, following §2 conventions exactly, with schema `Module.Attendance` (§2.2), and every one implementing `ITenantScoped` (§2.8).

Each layer gets an `Attendance` folder next to its `HR` folder. There are no separate Attendance projects: the layered structure (§9) replaced the earlier `robeem.Attendance.DataModel` / `robeem.Attendance` plan.

| Layer | Where Attendance code goes |
|---|---|
| Entities | `src/robeem.Domain/Attendance/`. The namespace stays `robeem.DataModel`, like the HR entities. |
| DTOs (requests, responses, list items) | `src/robeem.Contracts/Attendance/`, namespace `robeem.Contracts.Attendance`. |
| Services, processing engine, schedule resolution | `src/robeem.Application/Attendance/`. Add each `DbSet` to `IRobeemDbContext`, and register services in `AddRobeemApplication`. |
| EF mapping | `RobeemDbContext` `DbSet`s, plus `IEntityTypeConfiguration` classes under `src/robeem.Infrastructure/Persistence/Configurations/Attendance`. |
| Vendor adapters (`IAttendanceProvider` and `IAttendanceEventInterpreter`, both defined in Application) | `src/robeem.Infrastructure/Attendance/Providers`. Register both as **keyed services under the vendor's `ProviderKey`** in `AddRobeemInfrastructure`. Ingestion resolves the adapter, and normalization resolves the interpreter, by the integration's provider key. A vendor's timestamp formats and event-type codes live only in its interpreter. A new vendor is these two classes plus their registrations (invariant 9). |
| Background jobs (P5, P10) | Hosted services in `robeem.Api`, which call Application services through `ITenantScopeFactory`. |
| REST endpoints | `src/robeem.Api/Controllers`, under `api/attendance/...`. |
| Screens | `src/robeem.Web`, calling the Api through a typed client in `ApiClients/`. |

**HR must not depend on Attendance.** No file in an `HR` folder (Domain, Application, Contracts) may use an Attendance type. Attendance may use HR types (`Profile`, `TimeSchedule*`).

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

Run from the repository root. Requirements: .NET SDK 10, `dotnet-ef` 10 (`dotnet tool install -g dotnet-ef`), and a local PostgreSQL server (development uses `localhost:5432`, user `postgres`).

**Connection strings with passwords are never committed.** The Api's `appsettings.Development.json` lists the tenants with password-less connection strings. The full strings live in the Api project's user-secrets (`UserSecretsId` `robeem-attendance-api`), which override them by array index:

```bash
dotnet user-secrets set "Tenancy:Tenants:0:ConnectionString" "Host=localhost;Database=robeem_demo;Username=postgres;Password=<pwd>" --project src/robeem.Api
dotnet user-secrets set "Tenancy:Tenants:1:ConnectionString" "Host=localhost;Database=robeem_acme;Username=postgres;Password=<pwd>" --project src/robeem.Api
```

In other environments, supply them through environment variables (`Tenancy__Tenants__0__ConnectionString`) or a secret store. The Web needs only `RobeemApi:BaseUrl`, the Api's absolute URL. It is validated at startup.

| Item | Value |
|---|---|
| Solution | `robeem.attendance.slnx` |
| Api host (also the `dotnet ef` startup project) | `src/robeem.Api` (http://localhost:5181) |
| Web front end | `src/robeem.Web` (http://localhost:5180), calls the Api at `RobeemApi:BaseUrl` |
| Migrations project | `src/robeem.Infrastructure` (folder `Persistence/Migrations`) |
| DbContext | `robeem.Infrastructure.Persistence.RobeemDbContext` |
| Test project | `tests/robeem.Tests` (xUnit, in-memory SQLite) |

```bash
# Build (must finish with zero StyleCop errors, see §7)
dotnet build robeem.attendance.slnx

# Run the Api. In Development this migrates and seeds every configured tenant database
# (PostgreSQL databases robeem_demo and robeem_acme, created if missing) before it starts listening.
dotnet run --project src/robeem.Api --launch-profile http

# Run the Web front end (second terminal; it needs the Api running)
dotnet run --project src/robeem.Web --launch-profile http

# Add a migration (one schema for all tenants; each tenant database is migrated at startup)
dotnet ef migrations add <MigrationName> --project src/robeem.Infrastructure --startup-project src/robeem.Api --output-dir Persistence/Migrations

# Tests
dotnet test robeem.attendance.slnx

# Formatting check (part of every prompt's Definition of Done)
dotnet format robeem.attendance.slnx --verify-no-changes
```

There is no `dotnet ef database update` step. Tenant databases are migrated by `TenantDatabaseInitializer`, at Api startup, when `Database:InitializeOnStartup` is true. `dotnet ef` builds the model through `RobeemDbContextDesignTimeFactory`, which uses a placeholder tenant; set `ROBEEM_DESIGN_CONNECTION` to change its connection string.

---

## 6. Implementation roadmap

Checklist form of the full prompt series (see `PROMPT_PLAN.md` for the actual prompts to run, in order):

- [x] **P0** — Investigate. Done; the host was unavailable, so the decisions in §2.2, §2.3, §2.8 and §3.2 were made by the product owner (`DISCOVERY_NOTES.md`).
- [x] **Foundation** — Standalone solution: shared entities moved into the solution, missing host types written, tenancy, `RobeemDbContext` + `InitialHr` migration, HR employee screens, tests (§9).
- [x] **Layering** — Split into Domain / Contracts / Application / Infrastructure / Api / Web. Web is now an HTTP client of the Api (§9).
- [x] **P1** — Catalog/config entities: `AttendanceProvider`, `AttendanceIntegration`, `AttendanceDevice`, `AttendancePolicy` + EF configuration + migration (`AddAttendanceCatalog`; deviations in `ATTENDANCE_P1_NOTES.md`).
- [x] **P2** — `RawAttendanceTransaction` + `AttendanceProcessingBatch` + immutability enforcement (`AddAttendanceRawStorage`; deviations in `ATTENDANCE_P2_NOTES.md`).
- [x] **P3** — `NormalizedAttendanceTransaction` + composite-key unique index (filtered to `Unique` rows), plus `INormalizedTransactionRecorder`, through which every normalized row must be added (`AddNormalizedAttendanceTransactions`; deviations in `ATTENDANCE_P3_NOTES.md`).
- [x] **P4** — `IAttendanceProvider` abstraction + `GenericCsvProvider` + ingestion service (manual upload path first). Endpoint: `POST api/attendance/ingestion/{integrationId}/uploads`. Deviations in `ATTENDANCE_P4_NOTES.md`.
- [x] **P5** — Normalization engine (raw → normalized, employee/device resolution, duplicate detection). Includes:
  - `ExternalEmployeeIdMapping` (migration `AddExternalEmployeeIdMapping`);
  - `IAttendancePolicyResolver`, which P7 must reuse;
  - `AttendanceNormalizationHostedService`, a polling stand-in until P10.

  Deviations are in `ATTENDANCE_P5_NOTES.md`.
- [x] **P6** — `IScheduleResolutionService` (wraps `TimeSchedule`/`TimeScheduleLine`/`TimeScheduleTeam`/`TimeScheduleEmployee`). Deviations in `ATTENDANCE_P6_NOTES.md`.
- [x] **P7** — Attendance Processing Engine (`AttendanceRecord`, `AttendanceDaySummary`) using P6 + `AttendancePolicy`. Migration `AddAttendanceRecords`. Deviations are in `ATTENDANCE_P7_NOTES.md`. Rules later prompts must keep:
  - Read records with `IsCurrent`. A changed recalculation writes a new `Version` and retires the old one; nothing is updated in place.
  - The calculation is in `ShiftAttendanceCalculator`; the engine does the I/O.
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

**Every project in the solution must build clean under StyleCop.Analyzers.** A clean build means zero errors and, as of the foundation, zero warnings too; keep it that way.

### 7.1 Package reference — centralized

`Directory.Build.props` at the repository root adds `StyleCop.Analyzers` **1.2.0-beta.556** and `stylecop.json` to **every** project automatically. It also sets `net10.0`, nullable and implicit usings. **Do not add StyleCop to individual `.csproj` files**; a new project is analyzed simply by being under the repository root. Upgrading the analyzer is a deliberate, separate decision.

(The host robeem solution was never available. The sibling AssetManagementSystem has only a bare `StyleCop.Analyzers.Unstable 1.2.0.556` reference, the same analyzer build, so nothing needed reusing. See `DISCOVERY_NOTES.md` Q9.)

### 7.2 `.editorconfig` — per-rule severity, not a blanket switch

The root `.editorconfig` governs every project. **Do not set `TreatWarningsAsErrors`.** A rule set to `error` already fails the build.

| Tier | Severity | Rationale |
|---|---|---|
| Naming & readability (SA1300s, SA13xx member naming) | `error` | Catches inconsistency with §2's conventions. |
| Ordering, spacing, blank-line layout (SA1200s, SA1500s, SA1000s) | `warning` | `dotnet format` fixes most of these. |
| Documentation (SA16xx), file headers (SA1633), SA0001 | `none` | The supplied robeem files carry no XML docs or headers. |

Decisions recorded in `.editorconfig` (each has a comment there):

- **SA1300 and the brand name `robeem`:** the company name is all lower-case by branding decision.
  - It is exempted **only for that exact identifier** by a Roslyn suppressor, `src/robeem.Analyzers/BrandNameSuppressor.cs` (suppression id `RBM1300`). `Directory.Build.props` loads it into every project.
  - SA1300 stays an error for everything else; for example, a method named `badName` still fails the build.
  - The suppressor project can't load itself, so it gets a path exemption instead.
- **SA1309 `none`:** private fields are `_camelCase`.
- **SA1101 `none`:** robeem code never prefixes members with `this.`.
- **SA1124 `none`:** `#region not mapped` / `#region method` are house conventions (§2.6).
- **Line endings:** `*.cs` files use **CRLF**, and `dotnet format --verify-no-changes` fails on LF files. If a tool writes LF, run `dotnet format robeem.attendance.slnx whitespace` before finishing.
- `**/Migrations/*.cs` is generated code, so StyleCop ignores it.

### 7.3 `stylecop.json`

At the repository root. `Directory.Build.props` links it into every project; nothing to do per project.

### 7.4 Supplied files — path-scoped relaxation, never a rewrite

The 8 supplied files under `src/robeem.Domain` (`Core/Entity.cs`, `Core/SystemEntity.cs`, `Core/TimeShiftEntity.cs`, `HR/TimeSchedule*.cs`) must not be reformatted. `.editorconfig` relaxes only the rules they break, scoped to exactly those paths:

- SA1122, SA1133, SA1201, SA1208, SA1209, SA1312, SA1503, SA1507, SA1516;
- SA1300, for `isSystemEntity`/`isWorkflowEntity`;
- `charset = unset`, because three of them carry a UTF-8 BOM.

`Set.cs` and `EntityPropertyExtensions.cs` get SA1300 relief because the supplied files dictate their camelCase member names. **Never add a new file to these exemptions.** New files follow the rules.

### 7.5 Enforcement in practice

Definition of Done for every prompt:

- `dotnet build robeem.attendance.slnx` has zero errors and zero warnings;
- `dotnet test robeem.attendance.slnx` is green;
- `dotnet format robeem.attendance.slnx --verify-no-changes` passes.

Do not suppress a StyleCop finding with `#pragma warning disable`. Fix the code, or change the rule's severity in `.editorconfig` with a comment explaining why.

---

## 8. What *not* to do

- Do not create `WorkShift` or `EmployeeSchedule` entities — see §3.
- Do not create an entity without `ITenantScoped`. Do not set `TenantId` by hand, and do not call `IgnoreQueryFilters()` outside tests — see §2.8.
- Do not share a `RobeemDbContext` or a DI scope between tenants. Background work runs per tenant through `ITenantScopeFactory`.
- Do not enable `Tenancy:AllowUnauthenticatedTenantSelection` outside Development.
- Do not modify the 8 supplied files (§7.4), or move them out of namespace `robeem.DataModel`.
- Do not break the project dependency rules in §9. In particular:
  - `robeem.Web` must never reference Application, Infrastructure or Domain, or open a database connection;
  - Application must never reference Infrastructure.
- Do not use an Attendance type from an `HR` folder. HR does not depend on Attendance (§4).
- Do not return entities from the Api. Endpoints take and return `robeem.Contracts` DTOs.
- Do not let any code path `UPDATE` or `DELETE` a `RawAttendanceTransaction` row.
- Do not let the Payroll extraction query touch `RawAttendanceTransaction` or `NormalizedAttendanceTransaction`.
- Do not store a biometric template, under any field name, in `RawAttendanceTransaction.RawPayload` or anywhere else — allowlist fields at the adapter boundary.
- Do not set `TreatWarningsAsErrors` on any project as a substitute for the tiered `.editorconfig` severities in §7.
- Do not add cascade deletes. `RobeemDbContext` sets every foreign key to `Restrict` on purpose.

---

## 9. Solution layout

```
robeem.attendance.slnx
Directory.Build.props            net10.0, nullable, StyleCop + stylecop.json + brand-name suppressor for every project
.editorconfig / stylecop.json    analyzer configuration (§7)
src/
  robeem.Analyzers/              BrandNameSuppressor: SA1300 exemption for "robeem" only
  robeem.Domain/                 entities; namespace robeem.DataModel (SmartEnum: robeem.System.Contract)
    Core/                        Entity, SystemEntity, TimeShiftEntity (supplied); SmartEnum, ISeedData, IWorkflowEntity,
                                 ITenantScoped, Set, EntityPropertyExtensions (written here)
    HR/                          TimeSchedule* (supplied); Profile (the employee entity, written here)
  robeem.Contracts/              transport DTOs shared by Api and Web
    Common/                      CreatedResponse
    HR/Employees/                EmployeeInput, EmployeeListItem, ScheduleOption, EmployeeSaveResult
    Tenancy/                     TenantSummary, CurrentTenantResponse, TenancyProtocol (header + error codes)
  robeem.Application/            use cases; depends on Domain + Contracts (+ EF Core abstractions only)
    Persistence/                 IRobeemDbContext
    Tenancy/                     ITenantContext, ITenantRegistry, ITenantScopeFactory, TenantDefinition, exceptions
    Seeding/                     ISeedContributor
    HR/                          EmployeeService (+ schedule assignment), HrSeedContributor
  robeem.Infrastructure/         RobeemDbContext, entity configurations, migrations, tenancy implementation,
                                 SeedDataRunner, TenantDatabaseInitializer (Npgsql)
  robeem.Api/                    ASP.NET Core Web API host: api/employees, api/schedules, api/tenants;
                                 tenant middleware; migrates + seeds tenant databases at startup
  robeem.Web/                    ASP.NET Core MVC front end: Employees (list/add/edit), tenant picker;
                                 typed HttpClients for the Api (ApiClients/)
tests/
  robeem.Tests/                  xUnit: Set/indexer/cycle-day, tenant isolation, registry, seeding, employee service,
                                 Web API clients and tenant header forwarding
```

**Project dependencies** (an arrow means "references"; nothing else is allowed):

```
Domain        → (nothing)
Contracts     → (nothing)
Application   → Domain, Contracts
Infrastructure → Application
Api           → Application, Infrastructure, Contracts
Web           → Contracts
```

Attendance (P1 onward) adds an `Attendance` folder to each layer (§4).

**The employee entity is `Profile`.** The supplied `TimeScheduleEmployee` references it as `Profile? Employee`, so the class must be named `Profile` and live in `robeem.DataModel`. Fields: `EmployeeNumber` (unique per tenant), `FirstName`, `LastName`, `Email`, `Phone`, `Department`, `JobTitle`, `HireDate`, `TerminationDate`, `IsActive`. `Title` is kept equal to the full name.
