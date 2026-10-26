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
9. **New attendance vendors are additive configuration.** A vendor of exported files is configuration only: a vendor row, a connector row (no adapter key) and an `AttendancePayloadFormat`. A connector that is pulled over an API or pushes webhooks is configuration plus one adapter and interpreter pair, registered under the connector's adapter key. Neither may ever require a change to processing, exception, correction, or payroll code.
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

Implemented by `TimeSchedule`, `TimeScheduleShift`, `TimeScheduleTeam`, `TimeScheduleLine`, `TimeScheduleEmployee`. Apply it to new **catalog/reference** entities (`AttendanceVendor`, `AttendancePolicy`). Do **not** apply it to transactional entities (`RawAttendanceTransaction`, `NormalizedAttendanceTransaction`, `AttendanceProcessingBatch`) or to `Profile`.

**How it is consumed (this solution):** `ISeedData` is an empty marker interface (`src/robeem.Domain/Core/ISeedData.cs`).

- Seed data comes from `ISeedContributor` implementations (`src/robeem.Application/Seeding`) registered in DI. A contributor receives an `IRobeemDbContext`. An example is `HrSeedContributor` (`src/robeem.Application/HR/Seeding`), which seeds a "Standard Week" schedule (Mon–Fri 09:00–17:00) with its all-inclusive team.
- `SeedDataRunner` runs them for each tenant database every time `TenantDatabaseInitializer` runs: at startup when `Database:InitializeOnStartup` is true, which is the Development setting.
- **Contributors must be idempotent**: only add what is missing.
- The runner **rejects** any contributor that stages a change to an entity type not marked `ISeedData`, and saves nothing from it.
- Implementing the marker on a new type is always safe: it only *permits* seeding, and does nothing on its own.

The attendance catalog and the default attendance policy are seeded by `AttendanceSeedContributor` (P15, `src/robeem.Application/Attendance/Seeding`): the six transports, the device types, the Generic CSV and ZKTeco file formats, and the four providers. It adds only what is missing and never overwrites an administrator's edit. Entities it may stage changes to carry `ISeedData`: the catalog entities, and also `AttendanceDevice` and `AttendanceIntegration`, which the one-time moves below touch.

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

Seen on `TimeScheduleLine` and `TimeScheduleEmployee` — both are composite/junction-shaped rows with no single meaningful display string. Apply the same pattern to `RawAttendanceTransaction`, `NormalizedAttendanceTransaction`, and `AttendanceProcessingBatch`. For entities that *do* have a natural label (`AttendanceVendor.DisplayName`, `AttendanceException`, `AttendanceCorrection`), compose a readable `Title` instead (e.g., `$"{ExceptionType} — {AttendanceDate:d}"`).

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
- **Authentication is JWT bearer validation (decided in P12, below).** In production, `AllowUnauthenticatedTenantSelection` stays false, so a request reaches tenant data only with a valid token whose `tenant_id` claim names a configured tenant.

**Authentication and authorization — decided in P12 (2026-10-05, provisional: made on the assistant's recommendation when asked to continue; revisit before production).**

`robeem.Api` is a **resource server only**: it validates JWT bearer tokens (`Microsoft.AspNetCore.Authentication.JwtBearer`) from an external identity provider. **Entra ID is the target**, given the planned Azure move; any OpenID Connect provider works, since the Api only needs `Authentication:Jwt:Authority` and `Audience`. It has no user store, issues no tokens and never sees a password.

Why not the other two options:

- **ASP.NET Core Identity with cookies:** the Web front end reaches the Api server-to-server, so a browser cookie never arrives at the Api; the Web would have to log in on the user's behalf and forward something anyway. It would also make this solution own user storage, password reset and lockout.
- **Api-issued JWTs:** the same user store burden, plus token issuing, refresh and key rotation, to build what an identity provider already does.

What the token must carry (names are in `src/robeem.Api/Security/SecurityClaims.cs`; the IdP is configured to emit them):

| Claim | Meaning |
|---|---|
| `tenant_id` | the tenant database, read by `TenantResolutionMiddleware` (unchanged contract) |
| `role` (one per role) | `Employee`, `Manager`, `HRAdministrator`, `PayrollAdministrator`, `IntegrationAdministrator` (study §16.5) |
| `employee_id` | the user's `Profile` id: who proposes or decides a correction, and the actor in the audit log |

- **Roles are checked with `[Authorize(Roles = ...)]` on every Attendance endpoint, and on the HR ones.** There is no fallback policy: `GET api/tenants` stays anonymous for the tenant picker.
- **Development:** `Authentication:DevelopmentUser:Enabled` authenticates every request without a bearer token as a configured user with every role. The Api **refuses to start with it on outside Development**, as it does for `AllowUnauthenticatedTenantSelection`. It exists so the Web works locally.
- **Not done: Web sign-in.** The Web has no login and does not forward tokens, so in production its calls get 401. Add OpenID Connect sign-in to the Web and a handler that attaches the user's access token next to `TenantHeaderHandler`. Until then, the Web works in Development only.
- **Secrets:** `AttendanceConnection.SecretRef` (was `AttendanceIntegration.ConnectionConfigRef`) is a *reference*; `ISecretStore` resolves it to the secret under `Secrets:{tenantId}:{reference}` in configuration. Development: user-secrets. Production: any configuration provider, e.g. Azure Key Vault (`AddAzureKeyVault`) or environment variables; nothing in the code changes. Secrets are never logged or stored in a table.
- **Audit log:** `AttendanceAuditLog` (`SystemEntity`, insert-only) records every correction decision, every exception status change, and every read of raw or normalized attendance outside normalization and processing. `IAttendanceAuditTrail` stages the entry in the caller's unit of work, so it commits or rolls back with the change.

**On the Web side** (`src/robeem.Web/Tenancy`):

- The `/Tenants` picker stores the chosen tenant in the `robeem.tenant` cookie.
- `TenantHeaderHandler` forwards that cookie to the Api as `X-Tenant-Id`, but only when it is shaped like a tenant id.
- When the Api rejects the tenant, `TenantSelectionRequiredFilter` redirects GET requests to the picker. If the tenant was unknown, it deletes the cookie first.
- The Web has no tenant configuration of its own. The tenant list and the current tenant come from `GET api/tenants` and `GET api/tenants/current`.

**Background jobs and the scheduler — decided in P10 (2026-10-04): no Hangfire; one in-process scheduler in `robeem.Api`, Polly for resilience.**

`robeem.Api` runs a single `BackgroundService`, `ScheduledJobsHostedService`, which runs the `IScheduledJob`s in `src/robeem.Api/Attendance/Jobs`: `NormalizationJob` (every few seconds), `IntegrationSyncJob` (a short tick; each integration is due by its default connection's `AttendanceConnection.PollingSchedule`) and `ReconciliationJob` (nightly cron). Each job walks `ITenantRegistry.Tenants` and works inside its own `ITenantScopeFactory.CreateScope`. The logic is in Application services (`INormalizationService`, `IIntegrationSyncService`); a job only decides what is due. Per-integration retry with exponential backoff and jitter, and a circuit breaker, are Polly pipelines held by `IntegrationResilience`.

Why not Hangfire.PostgreSql (robeem.storage's choice):

- **Database per tenant.** Hangfire needs one storage database. This solution has no control database; it would have to be a tenant's, or a new connection and a new thing to provision. Job arguments would also carry tenant ids outside the tenant isolation this solution enforces.
- **The dashboard is a control surface with no authentication** until P12, and "do not expose it" is a rule that gets forgotten.
- **Retries would be doubled.** Hangfire retries a failed job on its own; the study wants retries and a breaker *per integration*, with health reported on the integration. Polly does that inside the sync, and Hangfire's job-level retry would fight it.
- **The workload is three periodic jobs.** The schedule already lives on the integration's default connection (`PollingSchedule`); there are no queues, continuations or fire-and-forget jobs to justify the dependency.
- **At-least-once is safe by design.** Overlapping sync windows and the duplicate detection of normalization (§1.4) absorb a repeated run.

What this gives up, and when to revisit:

- **Run a single Api instance.** There is no distributed lock. Two instances would sync the same integration twice (harmless: duplicates are absorbed) and could normalize the same raw row twice (not harmless). When the Api must scale out, adopt Hangfire.PostgreSql on a dedicated control database, or PostgreSQL advisory locks per tenant database, in place of this host. The Application services stay as they are.
- **Missed runs are not replayed.** After downtime, incremental sync catches up through its cursor; the nightly reconciliation waits for the next night.
- The circuit breaker state is in memory. A restart closes it; `AttendanceIntegration.HealthStatus` is what persists.

### 2.9 Lists that can grow are paged on the server — **a rule, decided 2026-10-25**

Every grid or list that is, or could become, long is **paged in the database query**. Long means: its rows come from a table that grows with time or with the size of the company (punches, batches, uploads, audit entries, job runs, exceptions, corrections, devices, mappings, employees). It is never loaded whole and then cut, paged in memory, or paged in the browser (no client-side table over a full dump). A row limit alone (`take`) is not paging: it hides the rest.

How it is done (the model is *Raw transactions*, *Normalized transactions* and *Attendance*):

- **Application:** the filter record ends with `Page` (1-based) and `PageSize`, and the service returns `PagedResult<T>` (`Items`, `Total`, `Page`, `PageSize`). The total is counted with the **same filters** as the page. The order is stable and has a unique tie-break (usually the id), so pages never overlap. A page past the end answers the **last page** (`PagedResult.Page` says which). The page size is clamped to 1..200; 0 means the default.
- **Default page size is 20**, maximum 200. The page sizes offered are 10, 20, 50, 100 and 200.
- **Api:** `page` and `pageSize` query parameters; a missing or non-positive page is 1. The action returns `PagedResult<T>`, never a bare list.
- **Web:** the controller takes `page` and `pageSize`, accepts only a size from the view model's `PageSizes` (anything else is the default), and fills a view model with `Page`, `PageSize`, `Total` and a `Pager` (`PagerModel`) whose route values carry **every filter** and the page size, so a page link never drops a filter. The view has a *Per page* select next to the filters and ends with `<partial name="_Pager" model="Model.Pager" />`. Do not write a second pager.
- **Audited reads** (raw and normalized attendance, §6) are audited per page, naming the page and the size.
- **Tests:** each paged list has a service test that the pages together are the whole list once, in order; that the total follows the filters; that a page past the end answers the last page; and that the size is clamped. The Web test checks that the pager keeps every filter.

What may stay unpaged: a catalog that is small by nature (vendors, connectors, payload formats, transports, device types, policies, time schedules, the connections of one integration), and the id-and-name option lists that fill a filter dropdown. If there is any doubt that a list stays small, page it. An option list that can grow with the employees or the devices (an employee picker) must become a search box, not a longer dropdown.

Paged so far: *Raw transactions*, *Normalized transactions*, *Attendance* (records), the audit log, the upload history, the employees list, the *Raw → normalized* page of one integration, and the payroll day list.

Lists that still break this rule, to be paged when they are next touched (new lists must not join them): the background job runs, the exceptions days, the correction and review queues, the Devices grid, the per-device employee-id mappings, the totals per employee on the Payroll page (one row per employee with an approved day), and the option lists that grow with the employees (the manager dropdown of the employee form, the development actor picker), which should become search boxes.

**Payroll is a special case.** Which days payroll may pay for is decided by `IPayrollAttendanceExtract`, which needs each employee's policy and so works in memory over the range (a pay period, bounded). It stays that way: it is the single gate (§1.8) and the CSV export wants the whole range. The screen reads through `IPayrollApprovedPageService` (`GET api/attendance/payroll/approved/page`), which orders, totals and cuts one page of the extract's result, so the Web never receives or draws more than a page of days. If a range ever gets too big for the extract itself, moving the eligibility checks into the query is a change to the extract, not to the page.

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
| Code adapters (`IAttendanceAdapter` and `IAttendanceEventInterpreter`, both defined in Application), **only for a connector that is not read from files** | `src/robeem.Infrastructure/Attendance/Providers` (today `SupremaBiostar*` and `GenericWebhook*`). Register both as **keyed services under the connector's `AdapterKey`** in `AddRobeemInfrastructure`; `IAttendanceAdapterCatalog` reports which keys have both. A connector of exported files has no adapter (null `AdapterKey`): its layout, timestamp patterns and event labels are an `AttendancePayloadFormat` row, read by `CustomFormatFileReader` and `CustomFormatInterpreter` (invariant 9). |
| Background jobs (P5, P10) | `IScheduledJob`s in `robeem.Api/Attendance/Jobs`, run by one hosted service (§2.8); they call Application services through `ITenantScopeFactory`. |
| REST endpoints | `src/robeem.Api/Controllers`, under `api/attendance/...`. |
| Screens | `src/robeem.Web`, calling the Api through a typed client in `ApiClients/`. |

**HR must not depend on Attendance.** No file in an `HR` folder (Domain, Application, Contracts) may use an Attendance type. Attendance may use HR types (`Profile`, `TimeSchedule*`).

| Entity | Base class | `ISeedData`? | Purpose (study ref) |
|---|---|---|---|
| `AttendanceVendor` (was `AttendanceProvider`) | `Entity` | Yes | The company (§5.3, §12.1): `VendorKey` (was `ProviderKey`), name, manufacturer, website, active. The key is the `SourceSystem` stored on raw punches and never changes. Managed at `api/attendance/vendors` and the Web's *Vendors* screen; deactivated, never deleted. How its data arrives is its connectors' business. The old transports text column was moved onto the vendor's connector and dropped (migration `DropLegacyCatalogColumns`) |
| `AttendanceConnectorDefinition` (+ `AttendanceConnectorTransport`) | `Entity` / `SystemEntity` | Yes | One software or protocol of a vendor (a vendor has many): `ConnectorKey` (unique, immutable), `VendorId` (immutable), `AdapterKey` (null: read by payload formats alone; otherwise the key a code adapter and interpreter are registered under), `DefaultPayloadFormatId`, `SupportsIncrementalSync`, `EncryptionKind` (`None` only until decryption exists) and its transports (file upload is implicit, every connector has it). An `AttendanceIntegration` is made with one (`ConnectorId`). Usable when it and its vendor are active and it has a default payload format or a registered adapter (`ConnectorUsability`). Managed at `api/attendance/connectors` and the *Connectors* screen; deactivated, never deleted. Named a connector, not a profile, because `Profile` is the employee entity (migrations `AddConnectorDefinitions`) |
| `AttendanceTransportDefinition` | `Entity` / `SystemEntity` | Yes | The tenant's names for the six `AttendanceTransport` members: display name, `IsPull`, `UsesPayloadFormat`, `IsActive`. Only the name and `IsActive` are editable (`api/attendance/transports`, *Transports* screen); behaviour is code, so no row can be added. File upload can't be switched off |
| `AttendanceDeviceType` | `Entity` | Yes | A kind of punch device (replaces the old enum of the same name); `AttendanceDevice.DeviceTypeId`. Managed at `api/attendance/device-types` and the *Device types* screen; deactivated, never deleted. The old device-type text column was moved into this table and dropped (migration `DropLegacyCatalogColumns`) |
| `AttendanceIntegration` | `Entity` | No | Tenant's configured integration, made with one of a vendor's connectors (§12.1) — carries the high-water mark, the payload format and key reference, and its connections. Its transport, secret reference and schedule are its **default connection's** (migration `AddConnections` moved them) |
| `AttendanceConnection` | `Entity` | No | A configured endpoint of an integration and how it is authenticated: `Transport`, `AuthMethod` (`ConnectionAuthMethod`), non-secret `Settings` (JSON text, only the keys `TransportDescriptors` declares), `SecretRef` (a reference, never a secret), `PollingSchedule`, `IsActive`, and the last test result. Exactly one is the integration's default (`IsDefault`, a filtered unique index). Managed at `api/attendance/integrations/{id}/connections` and the *Connections* screen; switched off, never deleted |
| `AttendanceDevice` | `Entity` | No | Physical/logical device mapped to a tenant (§13.2); `ConnectionId` (optional) names one of its integration's connections, else the default is used |
| `AttendancePolicy` | `Entity` | Yes | Rounding/grace/OT/interpretation/correction-workflow config (§13.2, §14.2) |
| `AttendanceProcessingBatch` | `SystemEntity` | No | One ingestion/normalization/processing/reprocessing run (§6.3, §12.4) |
| `RawAttendanceTransaction` | `SystemEntity` | No | Immutable vendor-exact copy of every punch (§6.2) |
| `NormalizedAttendanceTransaction` | `SystemEntity` | No | Vendor-neutral interpreted event (§7.3) |
| `AttendanceException` | `Entity` | No | Detected data-quality/policy issue (§9.3) |
| `AttendanceCorrection` | `Entity` | No | Proposed/approved fix to an exception (§11.2) |
| `AttendanceRecord` | `Entity` | No | Computed per-shift result (§13.2) |
| `AttendanceDaySummary` | `Entity` | No | Per-employee/day rollup of one or more records (§13.2) |
| `AttendancePayloadFormat` (+ `AttendancePayloadEventCode`; was `AttendanceFileFormat` / `AttendanceFileFormatEventCode`) | `Entity` | Yes | A file layout (delimiter, columns, timestamp patterns, type labels) that a manual-upload integration reads its files by: its own (`AttendanceIntegration.PayloadFormatId`), else its connector's `DefaultPayloadFormatId` (`PayloadFormatResolution`); `ConnectorId` (optional) says which connector it was made for; `PayloadKind` (was `FileKind`) is Auto, Csv or Excel until Stage 3 adds Json and Xml. A column setting may list several headers separated by `|`: the first non-empty value wins, and a file with none of them leaves the field empty. `PayloadColumns` is an optional allowlist of the columns kept in the raw payload. The built-in *Generic CSV* and *ZKTeco attendance export* formats are seeded rows (migrations `AddAttendanceFileFormats`, `AddDatabaseDrivenCatalog`) |
| `AttendanceUploadedFile` | `SystemEntity` | No | Insert-only record of each file a person uploaded: name, size, SHA-256 of its bytes, upload time, uploader, integration and the batch that stored its rows. The file itself is not kept. Shown on the *Upload attendance* screen and at `GET api/attendance/ingestion/uploads`; a file with the same SHA-256 as an earlier one is still ingested (raw storage tolerates duplicates) but reported as a repeat (migration `AddAttendanceUploadedFiles`) |
| `AttendanceJobRun` | `SystemEntity` | No | One run of a background job for one tenant, with its status (`Running`, `Succeeded`, `Failed`, `Missed`, `Cancelled`), trigger (`Scheduled`, `Manual`), work done and summary. Written by `JobCoordinator` through `IJobRunRecorder`/`IJobRunLog`; a quiet tick of an interval job is not stored, a cron job's and a manual run are always stored, and rows older than 30 days are pruned by the nightly reconciliation. Shown on the Web's *Background jobs* screen (HR and integration administrators view; only integration administrators Run now, for the current tenant) and at `api/attendance/jobs` (migration `AddAttendanceJobRuns`). Live state (running, next run, last check) is in memory in `JobCoordinator`; at startup interrupted runs are settled as Failed and each cron time that passed is recorded as Missed |
| `AttendanceReviewDecision` | `Entity` | No | A reviewer's additive decision on one `PendingReview` record version (follow-up) |
| `PayPeriod` | `Entity` | No | Minimal pay period (start, end, closed) for the payroll gate (P11, §18.3). A stand-in for a Payroll module that does not exist |

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
| Test project | `tests/robeem.Tests` (xUnit, in-memory SQLite; opt-in PostgreSQL tests, below) |

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

# Tests (in-memory SQLite; the PostgreSQL tests are skipped)
dotnet test robeem.attendance.slnx

# The PostgreSQL tests: migrations applied to an empty database, and the model's special cases on Npgsql.
# Set a connection string of a user who may create databases; each run creates and drops its own
# robeem_migtest_* database. Never put the password in a file that is committed.
#   PowerShell: $env:ROBEEM_TEST_POSTGRES = "Host=localhost;Username=postgres;Password=<pwd>"
#   bash:       export ROBEEM_TEST_POSTGRES="Host=localhost;Username=postgres;Password=<pwd>"
dotnet test robeem.attendance.slnx --filter "FullyQualifiedName~PostgresMigrationTests"

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
- [x] **P4** — `IAttendanceAdapter` abstraction (named `IAttendanceProvider` until the integration restructure) + a generic CSV reader (originally `GenericCsvProvider`, now the seeded *Generic CSV* file format) + ingestion service (manual upload path first). Endpoint: `POST api/attendance/ingestion/{integrationId}/uploads`. Deviations in `ATTENDANCE_P4_NOTES.md`. Since the upload-screen change, uploading is for `HRAdministrator` (not `IntegrationAdministrator`), on the Web's *Upload attendance* screen (`UploadsController`), which lists `GET api/attendance/ingestion/targets`: active integrations whose `Transport` is `FileUpload` (chosen per integration, migration `AddIntegrationTransport`; any provider may be given manual upload, otherwise the provider's own transports; a schedule is kept only for pulling transports) and whose provider is active, each with its layout (`ProviderMappingGuide`): its file format (`FileFormatLayout.Describe`), the integration's own else the provider's default; an integration with neither is still listed, with a layout that says to choose a format. The upload endpoint 404s for an integration that is not a target. `IngestionService.ReadUploadAsync` reads an upload by that format (`ICustomFormatFileReader`) and fails the batch, with the reason, when there is none; the raw rows keep the integration's own provider key. Normalization (`NormalizationService.GetIntegrationAsync`) interprets an integration that has a format by `ICustomFormatInterpreterFactory`, otherwise by the provider's keyed interpreter. Custom formats are managed by `IntegrationAdministrator` at `api/attendance/file-formats` (with a no-store sample `preview`) and the Web's *File formats* screen; a format is deactivated, never deleted, and a column that looks biometric can't be mapped (invariant 10).
- [x] **P5** — Normalization engine (raw → normalized, employee/device resolution, duplicate detection). Includes:
  - `ExternalEmployeeIdMapping` (migration `AddExternalEmployeeIdMapping`);
  - `IAttendancePolicyResolver`, which P7 must reuse;
  - `AttendanceNormalizationHostedService`, a polling stand-in until P10.

  Deviations are in `ATTENDANCE_P5_NOTES.md`.
- [x] **P6** — `IScheduleResolutionService` (wraps `TimeSchedule`/`TimeScheduleLine`/`TimeScheduleTeam`/`TimeScheduleEmployee`). Deviations in `ATTENDANCE_P6_NOTES.md`.
- [x] **P7** — Attendance Processing Engine (`AttendanceRecord`, `AttendanceDaySummary`) using P6 + `AttendancePolicy`. Migration `AddAttendanceRecords`. Deviations are in `ATTENDANCE_P7_NOTES.md`. Rules later prompts must keep:
  - Read records with `IsCurrent`. A changed recalculation writes a new `Version` and retires the old one; nothing is updated in place.
  - The calculation is in `ShiftAttendanceCalculator`; the engine does the I/O.
- [x] **P8** — Exception detection (`AttendanceException`): missing punches, invalid sequences. Migration `AddAttendanceExceptions`. Deviations are in `ATTENDANCE_P8_NOTES.md`. Rules later prompts must keep:
  - Detection is `AttendanceExceptionDetector`, run by the engine after the pairing. An exception belongs to employee + date + shift, not to a record version.
  - A shift with an exception that is not `Closed` is never `Approved`. `PendingCorrection` counts as not closed, so P9 closes an exception (`ResolutionMethod.Correction`) only when its correction is applied.
  - The engine closes an `Open` exception whose problem is gone as `AutoResolved`; it never touches a `PendingCorrection` one.
  - `IWorkflowEntity` goes on `AttendanceCorrection` (P9), not on `AttendanceException`.
- [x] **P9** — `AttendanceCorrection` (`IWorkflowEntity`) + `IAttendanceCorrectionService` + `api/attendance/corrections`. Migration `AddAttendanceCorrections`. Deviations are in `ATTENDANCE_P9_NOTES.md`. Rules later prompts must keep:
  - Only a `MissingPunch` exception can be corrected; a correction supplies that punch. The engine reads `Applied` corrections as punches with source `Corrected` (`AttendancePunch.NormalizedTransactionId` is null for them); no punch row is changed.
  - Approving, applying, closing the exception and recalculating the day is one transaction.
  - The requester and the decider come from the authenticated user (P12), and a manager decides only their direct reports' corrections (follow-up, `ATTENDANCE_FOLLOWUP_NOTES.md`).
- [x] **P10** — Scheduled polling ingestion job, incremental sync via high-water mark, reconciliation job. No migration; scheduler decision in §2.8. Deviations are in `ATTENDANCE_P10_NOTES.md`. Rules later prompts must keep:
  - The cursor moves only after the batch's raw rows are committed; reconciliation never moves it.
  - Retry and the circuit breaker wrap the provider call only, per tenant + integration; `HealthStatus` is `Degraded` after a failed sync, `Unhealthy` once the breaker has opened, `Healthy` after a success.
  - An adapter without incremental sync throws `NotSupportedException`; the sync job skips it rather than counting a failure.
- [x] **P11** — HR/Payroll extraction contract (Approved-only): `IPayrollAttendanceExtract`, `PayPeriod` (migration `AddPayPeriods`), and NetArchTest architecture tests. Deviations are in `ATTENDANCE_P11_NOTES.md`. Rules later prompts must keep:
  - Anything outside Attendance reads attendance only through `IPayrollAttendanceExtract`; the architecture tests enforce that Web, Contracts, and non-Attendance Application code never touch raw or normalized rows.
  - A day is returned whole or not at all: its summary and every current record must be `Approved`.
  - No path approves a `PendingReview` record yet, and a correction can only supply a missing punch: P12's review work must settle what "overtime pre-approved" means.
- [x] **P12** — Authentication (JWT bearer, provisional: §2.8), roles on every endpoint, `ISecretStore`, `AttendanceAuditLog` (migration `AddAttendanceAuditLog`), `api/attendance/payroll` and `api/attendance/audit`. Deviations are in `ATTENDANCE_P12_NOTES.md`. Rules later prompts must keep:
  - Every controller action names its roles (a test fails otherwise); the actor of a correction is the authenticated user, never a request field.
  - Every correction decision, exception status change and direct read of raw or normalized attendance outside normalization and processing writes an audit entry through `IAttendanceAuditTrail`; the audit log is insert-only.
  - A secret is resolved from `ConnectionConfigRef` for one call and never logged or stored.
  - Still open: Web sign-in (the Web works in Development only). Overtime approval and approver scope were closed in the follow-up below.
- [x] **Follow-up** — a batch for every sync (empty ones pruned nightly), an optional `Profile.ManagerId` that scopes approvers (migration `AddEmployeeManager`), and `AttendanceReviewDecision` + `IAttendanceReviewService` + `api/attendance/reviews` for approving PendingReview records (migration `AddAttendanceReviewDecisions`). Deviations and open points are in `ATTENDANCE_FOLLOWUP_NOTES.md`. Rules later prompts must keep:
  - A review decision belongs to one record version; payroll requires the approving decision of the *current* version for overtime under a pre-approval policy.
  - A record with an open exception is never reviewable; nobody reviews their own record.
  - A manager sees and decides only direct reports; HR covers employees with no (or an inactive) manager.
- [x] **P13** — Observability: `IIntegrationHealthService` (six KPIs + stored health + breaker state) and `GET api/attendance/integrations/{id}/health`. No new schema, no metrics package, and no alerts yet. Deviations are in `ATTENDANCE_P13_NOTES.md`. Rules later prompts must keep:
  - The health query writes nothing but its audit entry; `HealthStatus` is written by the sync job alone.
  - A rate is `null` when the window has nothing to measure, never 0.
- [x] **P14** — Test consolidation and `TEST_COVERAGE.md`: Scenario 4 end to end (`AttendanceEndToEndTests`), the §14.1 sequence table (`AttendanceSequenceTableTests`), two invariant tests. `TEST_COVERAGE.md` maps each scenario, decision-table row and invariant to its tests and lists the real gaps (the study was never readable, and the PostgreSQL tests exist but are opt-in and not yet run, among others). Keep it current: a prompt that adds behaviour adds its row.
- [x] **Transaction screens** — read-only *Raw transactions* and *Normalized transactions* screens for `HRAdministrator` and `IntegrationAdministrator` (`AttendanceRoles.TransactionViewers`): `IAttendanceTransactionQuery` (`src/robeem.Application/Attendance`), `api/attendance/transactions/{raw,normalized,integrations}`, `TransactionsApiClient` and two Web controllers. No migration. Rules later prompts must keep:
  - Filters, newest first. *Raw transactions* is paged on the server (`GET api/attendance/transactions/raw` takes `page` and `pageSize`, default 20, at most 200, and answers a `PagedResult<RawTransactionListItem>`; a page past the end answers the last; the pager is `_Pager` with the filters kept on every link; each page read is audited with its page and size). *Normalized transactions* is paged the same way (`GET api/attendance/transactions/normalized`, `PagedResult<NormalizedTransactionListItem>`). The rule for every list is §2.9. Order and date-filter on `Id`, `DateTime` columns or `ImportBatch.StartedAt`, never a `DateTimeOffset` (SQLite).
  - Every list writes an `AttendanceDataRead` audit entry before it reads. The integration options read no punches and are not audited.
  - No edit or delete path: a raw row is never changed from a screen (invariant 2).
- [x] **Integration time zone** — `AttendanceIntegration.TimeZoneId` (optional IANA zone; migration `AddIntegrationTimeZone`) and `POST api/attendance/integrations/{id}/retry-failed` with the Web's *Retry failed punches* button. Rules later prompts must keep:
  - A punch with no UTC offset is read in its device's zone, else the integration's, else it fails. A registered device's zone always wins; an unknown device is still flagged `FlaggedUnrecognizedDevice` even when the integration's zone read the time.
  - Retry sets a raw row from Failed back to Pending only when its error starts with `NormalizationFailures.NoTimeZonePrefix`; a punch rejected at ingestion is never revived. Only `ProcessingStatus` and `ProcessingError` change (invariant 2).
- [x] **Device for uploaded files** — `AttendanceIntegration.DefaultDeviceId` (migration `AddIntegrationDefaultDevice`) and `POST api/attendance/integrations/{id}/renormalize-flagged` with the Web's *Re-normalize flagged punches* button. Rules later prompts must keep:
  - For a `FileUpload` integration with a default device, every punch is attributed to it (`RawBatchWriter` sets `raw.DeviceId` and the zone; `NormalizationService` uses it before any lookup), ignoring the file's device id, which stays on the raw row as sent. Any other transport ignores it. The device must be one of the integration's own, and can't be deleted while chosen.
  - A punch flagged `FlaggedUnrecognizedDevice` is left out of the day by the engine. Re-normalizing never edits a punch: the flagged row becomes `ProcessingStatus.Excluded` (history, shown as *Replaced*), its raw row goes back to `Pending`, and a new normalized row is written. Read paths that pair a raw row with its normalized row ignore `Excluded` rows.
- [x] **Devices page and per-device employee mapping** — devices have a page of their own (`DevicesController`, *Devices* in the menu: a grid of every integration's devices, with add, edit and delete; `GET api/attendance/integrations/devices` and `.../{id}/devices/{deviceId}`), and the vendor-id → employee mapping (`ExternalEmployeeIdMapping`) belongs to a **device**, not an integration (migration `MoveEmployeeMappingToDevice`; endpoints `api/attendance/integrations/{id}/devices/{deviceId}/mappings`). The integration page only links to the devices. Rules later prompts must keep:
  - Normalization resolves the punch's device first (the default device of an upload, else the raw row's device, else by external id), then the employee through **that device's** mappings. A punch of an unrecognized device has no employee and is flagged `FlaggedUnrecognizedDevice` (device before employee); a registered device without the id is `FlaggedUnrecognizedEmployee`. So a mapping must exist on the device that receives the punches, including an integration's device for uploaded files.
  - The mapping is unique per `(TenantId, DeviceId, ExternalEmployeeId)`; the same vendor id may mean different people on different devices.
  - A device that has mappings can't be deleted (no cascade): remove them first. A device with punches on record still can't be deleted either.
  - The migration copies each existing mapping to every device of its integration; a mapping of an integration with no device is dropped, because there is no device to put it on.
- [x] **Records filter and paging** — the *Attendance* (records) list filters by employee and is paged on the server. `GET api/attendance/records` takes `page` (1-based) and `pageSize` (default 20, at most 200) and answers a `PagedResult<RecordDayListItem>` (`Items`, `Total`, `Page`, `PageSize`); `take` is gone. `GET api/attendance/records/employee-options` lists the employees the user may see (HR everyone, a manager their reports and themselves, an employee themselves) for the filter. No migration. Rules later prompts must keep:
  - The total is counted with the same scope and filters as the page, in the Application service; a page past the end is answered with the last page (`PagedResult.Page` says which). Order is stable (date, then name, then id) so pages don't overlap.
  - The shared pager is `Views/Shared/_Pager.cshtml` with `PagerModel`; every page link keeps the filters and the page size. Another list that needs paging reuses `PagedResult<T>` and the pager rather than adding `take`.
- [x] **Proposing a correction takes a time, not a date-time** — the *Exceptions* form asks for a time only (`<input type="time">`); the Web composes the day itself. Rules later prompts must keep:
  - The proposed time must be on the exception's attendance date. The day after is allowed only when the exception's shift ends after midnight (`ExceptionListItem.ShiftIsOvernight`, derived from the shift's times, never stored); the form then offers an *After midnight* checkbox. The Api enforces this (`AttendanceCorrectionService.ProposeCorrectionAsync`), whatever a client sends.
  - Nothing later than now can be proposed (the Api compares the instant, in the employee's time zone); the form also caps the time at the current time on today's date. The Api stays the authority.
- [x] **Break rules** — `AttendancePolicy` decides how a shift's break is judged (migration `AddBreakRules`; defaults of a new policy and of every existing one: `IgnoreBreaksShorterThanMinutes` 10, `BreakStartGraceMinutes` 15, `LongBreakGraceMinutes` 5, long/late/early do not raise, no-break raises as before). Rules later prompts must keep:
  - A break shorter than `IgnoreBreaksShorterThanMinutes` (a break-out followed at once by its break-in) is a toilet visit: `ShiftAttendanceCalculator` leaves both punches out, so the time is work and no break rule sees it. A break-out with no break-in yet is not short. 0 turns it off.
  - The calculator always computes `BreakFindings` (`LongBreak`: total break over the scheduled length plus the allowance; `LateBreak`/`EarlyBreak`: the first break-out away from the scheduled start by more than `BreakStartGraceMinutes`; `NoBreak`: a scheduled break and none taken, with check-in and check-out present) and stores it on the `AttendanceRecord`, whatever the policy raises. It is part of the version comparison.
  - Whether a finding becomes an exception is the policy's: `RaiseExceptionOnLongBreak/LateBreak/EarlyBreak` (new `AttendanceExceptionType`s, tied to the break-out punch) and `RaiseExceptionOnNoBreak`. No break keeps raising the missing break-out/break-in (a person can supply them); unticked, working through the break is allowed and a shift with a break is complete without break punches.
  - A break exception has no punch to supply, so a manager of the employee, or HR, **accepts** it with a reason (`POST api/attendance/exceptions/{id}/accept`, `IAttendanceExceptionService`; never the employee, never a missing punch): it closes as `Accepted` with `ResolvedById`/`ResolutionNote`, is audited, and the day is recalculated. The engine does not raise an accepted break exception again while the same break (same shift, type and break-out punch) is found; a different break is a new exception.
  - A `PendingReview` record whose recalculation is unchanged but no longer needs review (all exceptions closed) becomes `Approved`; a reviewer's own decision is never changed.
- [x] **Changing the type of an out-of-order punch** — for an `InvalidSequence` exception (for example, a check-out that follows a check-out) the employee proposes the type the punch should have, instead of a time (migration `AddPunchTypeCorrections`; `ProposeCorrectionRequest.NewEventType`, `IAttendanceCorrectionService.ProposeTypeChangeAsync`, the *Change punch* form on the Exceptions page). Rules later prompts must keep:
  - Only a type that would settle the exception is offered. The engine works it out when it raises or recalculates the exception: it calculates the shift again with the punch given each other type, and keeps a type when this exception for that punch is gone and no more punches are out of order than before (`AttendanceException.SuggestedEventTypes`, with the punch's type and time in `RelatedEventType`/`RelatedEventAt`, so lists read no normalized rows). What a type leaves undone (a check-out still missing) is a separate missing-punch exception. When no type settles it, nothing is offered and the employee can't propose.
  - It is an additive correction (`AttendanceCorrection.Kind` = `ChangePunchType`, with `NormalizedTransactionId` and `OriginalEventType`): the normalized row is never edited. Once applied, `AttendanceProcessingEngine` reads the same punch with the new type and source `Corrected`, and the record's `SourceCorrectionIds` lists it. A supplied-punch correction (`Kind` = `SupplyPunch`) is the only kind that adds a punch.
  - The same workflow and approvers as any correction apply (policy `CorrectionApprovalWorkflow`; a rejection reopens the exception). Proposing reads the normalized punch, so it writes an `AttendanceDataRead` audit entry.
- [x] **Correct a whole day** — the *Exceptions* page lists **days** (one row per employee and date, with a **Correct** button), and *Correct* opens every punch of the day to change a punch's type, add a missing punch or ignore one that should not count (migration `AddDayCorrections`; `IAttendanceDayCorrectionService`, `GET`/`POST api/attendance/corrections/day`). It replaces the per-exception forms on the page; the per-exception API (`ProposeCorrectionAsync`, `ProposeTypeChangeAsync`) is still there. Rules later prompts must keep:
  - A **request** is the unit of approval: all the edits of a day share `AttendanceCorrection.RequestId` and are approved, applied or rejected together (`DecideCorrectionAsync` acts on the whole request; the approver queue lists one item per request, with `Edits`). A correction proposed for one exception is a request of its own. A day-level correction has no `AttendanceExceptionId`: the day's open exceptions wait (`PendingCorrection`) for it together and are closed or reopened with it. Whatever the edits did not settle is found again by the recalculation and opens as a new exception.
  - Three kinds of edit, all additive (the normalized and raw rows are never touched): `SupplyPunch` adds a punch of any type at a time (on the day, or the day after for a shift that ends after midnight; never in the future); `ChangePunchType` gives an existing punch another type; `IgnorePunch` leaves it out of the calculation (`AttendanceProcessingEngine` removes it before calculating). Only punches that count can be changed or ignored; duplicates the normalization already left out, punches of unrecognized devices and punches already ignored are shown but not editable.
  - Only the employee the day belongs to can open or correct it, one request at a time per day, and only a day with an open exception. Reading the day's punches is audited (`AttendanceDataRead`).
- [x] **Integration restructure** — vendor → connector → payload format, flexible timestamps, per-device connections (stages 0–4 done; payload-format versioning followed; the SFTP/database/SDK fetchers are the open piece). The approved plan is `INTEGRATION_RESTRUCTURE_PLAN.md`; this bullet grows a line per finished stage.
  - **Naming note:** roadmap entries above this one were written before the rename and use the names of their time. Read *provider* as *vendor* (`AttendanceProvider` → `AttendanceVendor`, `ProviderKey` → `VendorKey`, `ProviderId` → `VendorId`, `ProviderUsability` → `VendorUsability`), and *file format* as *payload format* (`AttendanceFileFormat` → `AttendancePayloadFormat`, `FileKind` → `PayloadKind`, `FileFormatId` → `PayloadFormatId`, `FileFormatLayout` → `PayloadFormatLayout`, `FileFormatResolution` → `PayloadFormatResolution`). Routes: `api/attendance/providers` → `vendors`, `api/attendance/file-formats` → `payload-formats`, `api/attendance/integrations/providers` → `integrations/vendors`. The `UploadTarget` / `VendorMappingGuide` list of file extensions, once `FileFormats`, is `FileExtensions`. An integration's `Transport`, `ConnectionConfigRef` and `PollingScheduleCron` are now its default `AttendanceConnection`'s `Transport`, `SecretRef` and `PollingSchedule` (Stage 4).
  - **Stage 0 (done):** `IAttendanceProvider` is now `IAttendanceAdapter` (the code adapter).
  - **Stage 2a (done): the mechanical rename**, no behaviour change. Migration `RenameVendorAndPayloadFormat` renames tables, columns, indexes, primary keys and foreign keys in place (hand-written: the scaffold is a drop and a create). The adapters (`SupremaBiostarProvider`, `GenericWebhookProvider`) and their `Infrastructure.Attendance.Providers` folder keep their names for now (they are adapters, not vendors). Rule later prompts must keep: never rewrite a `VendorKey` value, since raw punches store it as `SourceSystem`.
  - **Stage 2b (done): the connector layer.** Migration `AddConnectorDefinitions` (hand-written, with a SQL backfill; the scaffold would have read vendor ids as connector ids): every vendor gets one connector carrying its key, its transports (the Webhook transport where it was flagged `SupportsWebhooks`), its default payload format and support flags; every integration is pointed at its vendor's connector; then `AttendanceIntegration.VendorId`, `AttendanceVendorTransport`, and the vendor's `DefaultPayloadFormatId` / `SupportsWebhooks` / `SupportsIncrementalSync` are dropped. A format that is the default of exactly one connector is made for it (`ConnectorId`). Deviations from the plan: a connector keeps a nullable `DefaultPayloadFormatId` (no `IsDefault` flag on the format, which would leave a format shared by several connectors with no default); `Manufacturer` stays on the vendor (it was not always equal to the name). Rules later prompts must keep:
    - The code that reads an integration is chosen by `integration.Connector.AdapterKey`; the raw rows' `SourceSystem` is the *vendor's* key. The two are different keys now.
    - A connector's key and vendor never change; neither does an integration's connector.
    - The webhook is accepted when the integration, its connector and its vendor are active and the connector lists the Webhook transport (there is no `SupportsWebhooks` flag).
    - Seeding creates a vendor and one connector (same key) per built-in vendor and never overwrites an administrator's edit; a vendor's legacy transport text moves to its connector.
  - **Stage 1 (done): timestamp spec.** A format with a date column and a time column may give `DateFormats` and `TimeFormats`; the text read is `date time`, tried against every date pattern followed by each time pattern (`PayloadFormatLayout.EffectivePatterns`, at most 10 patterns per list). With neither list set, `TimestampFormats` still describes the joined text, so formats saved before it keep reading as they did. A single timestamp column ignores both lists (cleared on save). `TimestampCulture` (empty = invariant) sets the language of month and day names; `TimestampCultures.Resolve` always reads the Gregorian calendar. Migration `AddTimestampSpec` (three additive columns, no data step). Rules later prompts must keep:
    - The text stored with a punch is always re-parseable by the format's effective patterns: in separate mode a workbook's real date and time cells (`TabularData.Dates`) are written out in the format's *first* date and time pattern, never through the text the cell displays. The single-column text path is unchanged (`dd-MMM-yy HH:mm:ss` for a date-time cell).
    - Two patterns that differ only in day/month order are allowed and the first listed wins; the preview warns (`PayloadFormatPreview.Warnings`) and shows every pattern that reads a row (`PatternMatches`, via `ITimestampExplainer`). `TimestampPatternCatalog` (Contracts) only offers starting points; a format stores the pattern text itself.
    - Not handled, and said so on the form: two-digit-year pivots (`TwoDigitYearMax`) and non-Latin digits.
  - **Stage 3 (done): JSON, XML and decryption.** `AttendancePayloadKind` gains `Json` and `Xml`, and a format a `RecordPath`; an integration gains `PayloadKeyRef`. Migration `AddStructuredPayloadsAndDecryption` (two additive columns, no data step). Rules later prompts must keep:
    - JSON and XML are read by flattening, not by a second mapping engine: `StructuredFile` (Infrastructure) turns each record the path selects into a row and every value inside it into a column named by its path (`emp.id`, `tags[1]`, `@code`, `Time`; repeated XML siblings are indexed), with a header row first. The rest (column mapping by `NormalizeHeader`, timestamp patterns, event codes, `PayloadColumns` allowlist, `RawPayloadSanitizer`) is the one CSV uses, so invariant 10 holds unchanged. `HasHeaderRow` is forced on for both kinds. A mapped column no record has is an error; one that only some records have is empty in the others.
    - The path syntax is deliberately small: JSON `a.b[*]` (a path that ends at an array names its elements; empty means a top-level array), XML `Root/Child` from the root (empty means the root's children). No XPath, no JSONPath, no new dependency.
    - The reader is hardened because the file comes from outside: 50 MB, JSON depth 64, 200 distinct fields, 1,000,000 records, and for XML no DTD (`DtdProcessing.Prohibit`, no resolver). Breaking a limit is an `InvalidDataException`, never a partial read.
    - Decryption sits in front of reading, never inside a reader: `IPayloadDecryptor` (Application, one per `PayloadEncryptionKind`), `IPayloadDecryptorCatalog`, implementations in `Infrastructure/Attendance/Decryption` (`Aes` CBC/GCM with the key, optional IV and mode in a JSON secret; `Pgp` via BouncyCastle with the armored private key and optional passphrase; `PasswordProtected` = a zip holding exactly one file, via SharpZipLib; a password-protected Office workbook is not read). `IngestionService.ReadUploadAsync` resolves `integration.PayloadKeyRef` through `ISecretStore` for that one call, decrypts to a temporary file and deletes it afterwards.
    - A decryptor never puts key material, a key id or the library's own message in an exception: a file the key does not open is always the fixed `DecryptionLimits.CannotDecrypt` text, and the output is capped at 100 MB. The key is a reference apart from `ConnectionConfigRef` (an integration of an encrypted connector must name one; any other must not).
    - A connector may be given an `EncryptionKind` only when a decryptor is registered for it, and is `Usable` only then (`ConnectorUsability`). Encrypted webhooks and pulled payloads are not decrypted yet; only uploaded files are.
  - **Stage 4 (done): connections and per-device transport.** Migration `AddConnections` (hand-written data step): every integration gets a default `AttendanceConnection` copying its `Transport`, `ConnectionConfigRef` (→ `SecretRef`, unchanged, so no secret-store key moves) and `PollingScheduleCron` (→ `PollingSchedule`), then those three columns are dropped from the integration; `AttendanceDevice.ConnectionId` and `AttendanceProcessingBatch.ConnectionId`/`PayloadFormatId` are added. The authentication method is derived (webhook → `HmacSignature`, BioStar's REST API → `SessionLogin`, everything else `None` until an administrator chooses). Deviations from the plan: the API is nested under the integration (`api/attendance/integrations/{id}/connections`, with `make-default` and `test`; descriptors at `api/attendance/transports/descriptors`) instead of a flat `api/attendance/connections`; `Settings` is `text`, not `jsonb`; a secret reference is optional when a connection is saved (a test says when it is missing); payload-format versioning came right after (below). Rules later prompts must keep:
    - What a transport needs is code (`TransportDescriptors`) and every connection is held to it by `ConnectionSettingsValidator`: only declared settings, required ones present, an authentication method the transport allows, a secret named by reference and never typed, a plain http(s) URL with no credentials. A setting whose name looks like a credential is refused with a pointer to the secret. SFTP requires its host key fingerprint.
    - The integration form still edits the **default connection** (`IntegrationInput.Transport`, `ConnectionConfigRef`, `PollingScheduleCron`, `AuthMethod`, `Settings`); an integration with no connection reads as a plain file upload, and its next save creates the default. Readers that need the transport ask the default connection, never a column on the integration.
    - Upload targets are integrations whose default connection is an active `FileUpload` (or none). Sync and polling go through the default connection; a switched-off default is skipped, not failed. Sync state (cursor, health, breaker) stays on the integration: one pulling connection per integration until a second is needed.
    - A batch records the `ConnectionId` it came through, and for an upload the `PayloadFormatId` it was read by. Webhooks: `POST api/attendance/webhooks/{tenantId}/{integrationId}` means the default webhook connection (else the lowest-id active one), `.../{integrationId}/c/{connectionId}` names it; the credential is checked by the connection's method (`HmacSignature` over the body, `ApiKey` in `X-Api-Key`, `BearerToken` in `Authorization`), always in constant time, and an unknown, inactive or secretless connection answers the same 401.
    - Testing a connection (`POST .../{id}/test`, audited as `ConnectionTested`) answers only fixed wording and records it on the connection: never the other end's reply, an exception text or the secret. It times out. Only a REST API with a registered adapter is really tried; a webhook is "ready" when its secret exists; SFTP, database and device-SDK connections say no adapter reads them yet (their fetching is the next piece of work).
    - A device's connection must belong to its own integration. The BioStar adapter reads `baseUrl` from the connection's settings first, its secret second.
  - **Contract migration (done).** `DropLegacyCatalogColumns` drops `AttendanceVendor.SupportedTransports` and `AttendanceDevice.DeviceType` (the `Legacy*` properties are gone from the code, and so is the seeder's move of their text). Before dropping, its SQL moves whatever the seeder never moved: a vendor's transports onto its main connector (the one with its key, else the oldest), a device's type onto a device type row (matched ignoring case, created when unlisted). `Down` restores the device types as text; the transports stay in the connector tables. `AttendanceDevice.DeviceTypeId` stays nullable (a device that never had a type). No `Legacy*` name may be added outside a migration window.
  - **Example files (done).** The payload format form can show and download an example file in CSV, JSON or XML, made from the form as it stands, saved or not (`PayloadFormatSampleBuilder`; `POST api/attendance/payload-formats/sample?kind=`; the Web's `PayloadFormats/Sample`). The example uses the format's own column names or paths, record path, delimiter, timestamp patterns, culture and type labels, with made-up data, and an unfinished format gets default names. Rule later prompts must keep: a change to what the reader accepts (a new path syntax, a new kind) changes the builder with it; `PayloadFormatSampleTests` read each example back through `CustomFormatFileReader`, so an example is never a file the format could not read.
  - **Payload-format versioning (done, after Stage 4).** `AttendancePayloadFormat.Version` and `SupersedesId`; migration `AddPayloadFormatVersions` (existing formats are version 1; every past upload batch is told the format its integration used). Rules later prompts must keep:
    - Editing a format in a way that changes how a file is read (`ReadingSignature`: kind, delimiter, header row, sheet, record path, every column, the allowlist, the patterns, the culture, the event codes) after any batch recorded it (`AttendanceProcessingBatch.PayloadFormatId`) creates the next version instead of changing the row. The old row is never edited again; integrations and connectors that pointed at it move to the new one. A name, a description, switching it off, or any edit before the format's first use is made in place.
    - Only the latest version of a name is listed, offered, edited or chosen (`!Any(newer => newer.SupersedesId == f.Id)`); an older one is read by id only. The name is unique per version, and a format's own earlier versions do not count as another format with its name.
    - Normalization reads a batch by the version the batch recorded (`Run.PayloadFormatId`), else by the integration's current format. So a punch that is normalized again, or retried, is read the way it was first read. A batch that records no format (a sync, a webhook) is unaffected.
- [x] **P15** — Seed data: `AttendanceSeedContributor` (provider catalog, default policy). Deviations are in `ATTENDANCE_P15_NOTES.md`. The rule it used to carry (a provider is seeded active only when its adapter is registered) was replaced by the database-driven catalog below: usability is computed, not seeded.
- [x] **Database-driven catalog** — providers, file formats, device types and transports are tenant data, managed by `IntegrationAdministrator` (never deleted, only deactivated). Migration `AddDatabaseDrivenCatalog` (additive; the old text columns were kept for a release as `LegacySupportedTransports` / `LegacyDeviceType`, then moved and dropped by `DropLegacyCatalogColumns`). The generic CSV and ZKTeco adapters and `IAttendanceFileReader` are gone: their layouts are seeded `AttendanceFileFormat` rows. Deviations and open points are in `ATTENDANCE_DBDRIVEN_NOTES.md`. Rules later prompts must keep:
  - An administrator's edit to a seeded row is never overwritten; the seeder links the built-in formats to their provider and upload integrations only in the run that creates them.
  - A connector is usable when it and its vendor are active and it has a default payload format or a registered code adapter (`ConnectorUsability`); never decide that by a seed flag.
  - A new vendor of exported files is data: a vendor row, a connector row and a payload format. Do not write an adapter for one. An adapter is for a connector that is pulled or pushes, and the connector's `AdapterKey` must equal the key it is registered under.
  - A transport's behaviour is code: rows describe the six `AttendanceTransport` members and cannot be added from a screen, and `IsPull` / `UsesPayloadFormat` are not editable.

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
- Do not add a grid or list that can grow without paging it on the server (§2.9): no unbounded list from the Api, no `take`-only limit standing in for paging, no paging in memory or in the browser.

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
