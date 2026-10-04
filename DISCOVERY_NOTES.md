# Prompt 0 — Discovery Notes

**Date:** 2026-10-03 · **Scope:** read-only discovery. No feature code was written.

## Decisions after discovery (2026-10-04)

The product owner answered the open questions. The host robeem codebase will not be provided; the developer is external and has only the 8 shared entities. So this repository became a **standalone solution** that recreates what the shared entities need. CLAUDE.md carries the binding version of each decision; the findings below this section are kept as the historical record.

| # | Question | Decision | Where it lives |
|---|---|---|---|
| 1 | Multi-tenancy | **Database per tenant**, plus a `TenantId` on every entity. The column is a CLR property on new entities (`ITenantScoped`) and a shadow property on the supplied ones. It is stamped on insert; cross-tenant writes throw; a global query filter covers reads. | CLAUDE.md §2.8 |
| 2 | `SmartEnum.Module` | Defined here as a plain enum: `HR`, `Attendance`. Attendance entities use `Module.Attendance`. | §2.2 |
| 3 | `ISeedData` | "Must not break the code." It is a marker consumed by `SeedDataRunner`, which only allows marked types to be seeded and runs idempotent contributors per tenant at startup. | §2.3 |
| 4 | DbContext / repository | `RobeemDbContext` (EF Core 10, PostgreSQL via Npgsql; SQL Server until 2026-10-04) with `IEntityTypeConfiguration` classes. No repository layer: services use the DbContext directly as the unit of work. | §9 |
| 5 | Background jobs | None yet. Per-tenant work goes through `TenantScopeFactory`. The scheduler is chosen in P10; robeem.storage's Hangfire is the reference pattern. | §2.8 |
| 6 | Generic layer | "Must not break the code." The indexer helpers (`getPropertyValue`/`setPropertyValue`) and `Set.isSystemEntity`/`isWorkflowEntity` are implemented and tested. There is no generic grid; screens are explicit MVC views. | §2.7 |
| 7 | `AllInclusive` | It is the schedule's default team and contains every employee assigned to the schedule. Assigning a schedule puts the employee in that team. | §3.2 |
| 8 | Commands | Real values now exist: `robeem.attendance.slnx`, `src/robeem.Web`, `src/robeem.Infrastructure`, `RobeemDbContext`, `tests/robeem.Tests`. | §5 |
| 9 | SA1300 | `robeem` is exempt by a Roslyn suppressor that matches only that identifier. SA1300 is otherwise still an error. | §7.2 |

The employee entity the shared code expects is `Profile` (`TimeScheduleEmployee.Employee` is typed `Profile`). It was added in `src/robeem.DataModel/HR/Profile.cs`, together with employee list/add/edit screens in `src/robeem.Web`.

Verified on 2026-10-04:

- The build has zero errors and zero warnings.
- 37/37 tests pass.
- `dotnet format --verify-no-changes` passes.
- The app was run first against SQL Server LocalDB. After the switch to PostgreSQL the same day, it was run again against PostgreSQL 18, creating, migrating and seeding the `robeem_demo` and `robeem_acme` databases. Creating, editing and listing employees, plus validation, worked over HTTP. A POST without an antiforgery token returned 400, and an unknown tenant header returned 400. The Acme tenant saw none of Demo's employees.

## Headline: the host robeem solution is not available

This repo (`robeem.attendance`) contains only 8 source files, copied from the host's `robeem.DataModel` project:

```
robeem/DataModel/Entity.cs            robeem/DataModel/TimeSchedule.cs
robeem/DataModel/SystemEntity.cs      robeem/DataModel/TimeScheduleShift.cs
robeem/DataModel/TimeShiftEntity.cs   robeem/DataModel/TimeScheduleTeam.cs
robeem/DataModel/TimeScheduleLine.cs  robeem/DataModel/TimeScheduleEmployee.cs
```

The repo has no `.sln`/`.slnx`, no `.csproj`, no DbContext, no startup code, and no tests. The files don't compile on their own. They reference six symbols defined nowhere in the repo: `robeem.System.Contract.SmartEnum`, `ISeedData`, `Set`, `Profile`, and the extension methods `getPropertyValue`/`setPropertyValue`.

I searched for the host solution beyond this repo:

- **Whole `D:\Workplace` tree:** searched `.cs`/`.csproj`/`.sln*` files for `SmartEnum`, `ISeedData`, `robeem.System.Contract`, `robeem.DataModel`, `class Profile`, `HasQueryFilter`, `TenantId`.
- **`D:\` and `C:\Users\marounbm`:** searched by file name for `SmartEnum*.cs`, `ISeedData*.cs`, `robeem.System*`, `robeem.DataModel*`, `Profile.cs`, `Set.cs`, `*robeem*.sln*`.

Neither search found it. The two robeem-named solutions on disk are **different codebases**:

| Path | What it is | Relevant? |
|---|---|---|
| `D:\Workplace\robeem\robeem\robeem.slnx` | **AssetManagementSystem**: `AssetManagementSystem.{API,Domain,Infrastructure}`, `AmsDbContext`, `AEntity` base | No. It has a different entity base, different namespaces, and no `robeem.DataModel`. |
| `D:\Workplace\robeem\robeem.storage\Robeem.Storage.slnx` | Standalone file-storage microservice | Only as an analogy. It has its own database and conventions. |

**Consequence:** questions 1–8 can't be answered from code. For each one, I list what the 8 files prove, what stays unknown, and a safe default. **Q9 is answered fully**, verified by running the analyzer. To unblock Q1–Q8, someone needs to provide the host solution: the one containing the `robeem.DataModel` project, `robeem.System.Contract`, `Profile`, and the DbContext. Then re-run Prompt 0 questions 1–8 against it.

---

## 1. Multi-tenancy — **undetermined; needs host solution**

**What the 8 files prove:**

- No entity has a tenant column or a tenant-scoping base class or interface.
- `Entity` (`robeem/DataModel/Entity.cs:34-49`) maps only `Id`, `Title`, `CreatedOn`, `CreatedBy`, `ModifiedOn`, `ModifiedBy`, and `GUID`.
- `SystemEntity` (`robeem/DataModel/SystemEntity.cs:9-18`) removes the audit and GUID columns and adds nothing.
- So tenant scoping, if the host has any, is not done with a column on these entities.

**What I could not check:** a `HasQueryFilter` in the DbContext, a connection or schema resolver, tenant middleware, or claims-based resolution. All of those would live in host code that isn't available.

**Sibling evidence (analogy only, not the host):**

- **robeem.storage** resolves the tenant from the `X-Tenant-Id` header (`robeem.storage/src/Robeem.Storage.Api/Tenancy/HeaderTenantAccessor.cs:16`). It stores `TenantId` as a column and indexes on it (`.../Persistence/StorageDbContext.cs:60`). This is where the study's "TenantId on every table" recommendation came from.
- **AssetManagementSystem** uses `IOrganizationScoped.OrganizationSiteId` for row-level scoping (`robeem/AssetManagementSystem.Domain/Common/IOrganizationScoped.cs`). Its `TenantId` fields (`AssetCloud.cs:80`, `IncidentCustomerImpact.cs:32`) are business data, such as an Azure tenant ID, not app multi-tenancy.

**Inference (unconfirmed):** the host entity base has no tenant column. That fits database-per-tenant or connection-per-tenant, but a global filter on a shadow property is also possible.

**Answer:** undetermined. **Do not add `TenantId`** until the host's mechanism is confirmed. CLAUDE.md §2.8 is updated to say this.

## 2. `SmartEnum.Module` — **undetermined; needs host solution**

**What the files prove:**

- Every `TimeSchedule*` entity aliases `using SmartEnum = robeem.System.Contract.SmartEnum;` and uses `Schema = nameof(SmartEnum.Module.HR)`. See `TimeSchedule.cs:1,8`, `TimeScheduleShift.cs:8`, `TimeScheduleTeam.cs:8`, `TimeScheduleLine.cs:8`, and `TimeScheduleEmployee.cs:8`.
- **`HR` is the only confirmed member.**

**What stays unknown:**

- The other members.
- Whether `Module` is a plain `enum` or a class with static instances. `nameof(...)` compiles for both. The name "SmartEnum" suggests the class-based Ardalis-style smart-enum pattern, which would be data-ish rather than a plain enum.
- Whether any tooling, such as migrations or permissions, iterates `Module` values.

**Recommendation:** use **`Module.HR`** for all new entities until someone with host access decides otherwise. It is the only value known to exist, and moving tables to a new schema later is a mechanical migration. If the host shows `Module` is safely extensible, `Module.Attendance` is still the better long-term home for the high-volume raw/normalized tables. **This needs a human decision.** CLAUDE.md §2.2 is updated.

**Gotcha found while stubbing these types:** the host has a namespace `robeem.System`. Inside `namespace robeem.*`, an unqualified `System.Foo` reference resolves to `robeem.System.Foo` and fails. Keep `using System;` outside the namespace, as the existing files already do. Write `global::System.Foo` if a fully qualified name is ever needed.

## 3. `ISeedData` — **consumer undetermined; interface is a marker**

**What the files prove:**

- `ISeedData` is implemented by `TimeSchedule`, `TimeScheduleShift`, `TimeScheduleTeam`, `TimeScheduleLine`, and `TimeScheduleEmployee`.
- None of them declares any member that serves the interface. So it is a **marker interface**, or its members all have default implementations.
- Two of the implementers are `SystemEntity` types (`TimeScheduleLine`, `TimeScheduleEmployee`), so the consumer handles both base classes.

**What stays unknown:** where it is consumed, whether by a seeding service, a startup hook, or a CLI command, and whether it runs on every startup, on first run only, or on demand.

**Safety:** adding a marker to `AttendanceProvider` or `AttendancePolicy` is compile-safe. Whether it is runtime-safe depends on the consumer. If the consumer generates demo rows for every `ISeedData` type on every startup, production could receive fake providers. **Check the consumer before Prompt 1 adds `ISeedData`.** If it can't be checked, leave `ISeedData` off and add it in Prompt 15, the seed-data prompt.

## 4. DbContext and repository pattern — **undetermined; needs host solution**

**What the files prove:**

- Configuration is done at least partly through **data annotations**: `[Table(..., Schema=...)]`, `[Key]`, `[Required]`, `[MaxLength]`, `[Column(Order = n)]`, `[NotMapped]`, `[Serializable]`.

**What stays unknown:** the DbContext class, how its `DbSet`s are declared (explicitly, or registered by reflection over `Entity` subclasses), whether Fluent API or `IEntityTypeConfiguration` exists, and any repository or unit-of-work pattern and service/controller wiring.

**Hint that Fluent configuration may exist:** `TimeScheduleEmployee.TimeScheduleId` (`TimeScheduleEmployee.cs:12`) has no navigation property. With annotations alone, EF Core would create no FK constraint to `TimeSchedule`. Either Fluent configuration adds one somewhere, or no constraint exists.

## 5. Background jobs — **undetermined; needs host solution**

No job infrastructure is visible in the 8 files.

The sibling robeem.storage uses **Hangfire on PostgreSQL**. See `robeem.storage/src/Robeem.Storage.Api/Program.cs:3-4,88` and the jobs under `.../Infrastructure/Jobs/` (`RetentionExpiryJob`, `UsageReconciliationJob`). That is a reasonable pattern to propose **if** the host has no scheduler, but it is not evidence of what the host uses. Confirm before Prompt 5 and Prompt 10.

## 6. Generic UI/API layer — **undetermined; one indexer caveat found**

**What the files prove:**

- The definitions of `Set.isSystemEntity(Type)`, `Set.isWorkflowEntity(Type)`, and `getPropertyValue`/`setPropertyValue` are not available, and neither are their callers. So I can't confirm whether new entities need extra registration, such as a menu or module entry, a permission row, or a workflow marker.
- `isWorkflowEntity` suggests some types are flagged for approval workflow, perhaps by an interface or attribute. `AttendanceCorrection` (Prompt 9) will probably need that flag. **Find out what `isWorkflowEntity` checks before Prompt 9.**

**Indexer caveat** (`Entity.cs:17-27`): the setter redirects `this["Employee"] = 5` to `EmployeeId` only when `getPropertyValue("Employee")` currently returns a non-null `Entity`. That means only when the navigation property is already loaded. If the navigation property is null, the setter tries to assign the raw value to the navigation property itself. CLAUDE.md §2.4's `XId` + `X?` pairing is still required, but code that sets FKs through the indexer should load the navigation property first or set `XId` directly.

## 7. `TimeScheduleTeam.AllInclusive` — **no readers found**

- The only occurrence is the declaration `public bool AllInclusive { get; set; } = true;` (`robeem/DataModel/TimeScheduleTeam.cs:15`).
- No code in this repo, or anywhere on disk, reads it. The host code that would read it isn't available.
- Note that it **defaults to `true`**, so any team created without explicitly setting it is "all-inclusive".

**Recommendation for Prompt 6:** `IScheduleResolutionService` should **not** fall back to an `AllInclusive` team. An employee with no `TimeScheduleEmployee` row should produce an explicit "no schedule configured" result or exception. Revisit if the host shows existing fallback logic. CLAUDE.md §3.2 already says this.

## 8. Commands — **no real values exist**

| Placeholder | Finding |
|---|---|
| Solution | None in this repo. `robeem.slnx` on disk belongs to AssetManagementSystem. |
| API project | Unknown |
| Data / migrations project | Unknown |
| DbContext | Unknown. `AmsDbContext` is the AMS solution's, not the host's. |
| Test project | None |

CLAUDE.md §5 now records this and warns against running `dotnet ef` until the host is identified.

## 9. Static analysis — **answered and verified**

### Existing StyleCop configuration

**robeem.attendance (this repo):** only the supplied root `.editorconfig` and `stylecop.json`. There are no projects.

**robeem.storage:** nothing. There is no StyleCop reference, `.editorconfig`, `stylecop.json`, `.ruleset`, or `Directory.Build.props`, which matches the expectation.

**AssetManagementSystem (`robeem.slnx`):** one bare package reference in a single project, `AssetManagementSystem.Domain/AssetManagementSystem.Domain.csproj`:

```xml
<GenerateDocumentationFile>True</GenerateDocumentationFile>
...
<PackageReference Include="StyleCop.Analyzers.Unstable" Version="1.2.0.556">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

That solution has no `.editorconfig`, `stylecop.json`, `.ruleset`, or `Directory.Build.props`, and its API and Infrastructure projects are unanalyzed. **It has no tuned configuration worth reusing,** and it is a different codebase anyway. `StyleCop.Analyzers 1.2.0-beta.556`, the version pinned in CLAUDE.md §7.1, is the wrapper package for that same `Unstable 1.2.0.556` analyzer build, so the two don't conflict.

**Conclusion:** adopt the CLAUDE.md §7 configuration, with the corrections below.

### Private-field convention: `_camelCase`

**Counts of `private` fields in sibling code:**

| Location | `_camelCase` | `camelCase` |
|---|---|---|
| robeem.storage `src` | 4 | 0 |
| robeem.storage `tests` | 11 | 0 |
| AMS Infrastructure + API (`_db`, `_currentUser`) | 3 | 0 |
| AMS Domain (`domainEvents`) | 0 | 3 |

**robeem.DataModel itself:** it has no fields, but the author prefixes even locals with `_` (`TimeSchedule.cs:33,37,43,44`: `_lastScheduleDate`, `_difference`, `_dayDifference`, `_cycleDay`).

**Decision:** `_camelCase`. **SA1309 is set to `none`** in `.editorconfig`, rather than promoted to `error`, which would only have been correct for a `camelCase` convention. This is inferred from sibling code and the DataModel author's habits. Re-confirm against the host's service classes when they are available.

### Verification: running the supplied config over the existing files

I put copies of the 8 files in a scratch project at the real relative path (`robeem/DataModel/`). I added minimal stubs for the missing host symbols, added the root `.editorconfig` + `stylecop.json`, and referenced `StyleCop.Analyzers.Unstable 1.2.0.556`. I also added one new sample entity written exactly per CLAUDE.md §2.2 and §2.4. **The real repo files were not modified.**

**Glob check:** the §7.4 path override glob `**/DataModel/{...}.cs` **matches correctly** in the real layout. SA1101, SA1124, SA1201, and SA1516 were suppressed for the existing files.

**SA1312 = error broke `TimeSchedule.cs`:** the 4 `_`-prefixed locals were flagged. **Fixed:** added `SA1312 = none` to the pre-existing-files override in `.editorconfig`, and mirrored it in CLAUDE.md §7.4. SA1312 stays `error` for new code.

**SA1300 = error breaks every file, existing and new.** This is open and **needs a human decision before Prompt 1.**

- `Element 'robeem' should begin with an uppercase letter` fires on the namespace of all 8 files *and* on the new §2.2-compliant sample.
- It also fires on the host's camelCase methods `Entity.isSystemEntity` and `Entity.isWorkflowEntity` (`Entity.cs:51-52`).
- StyleCop has no setting to exempt namespaces, and the namespace `robeem.DataModel` is mandated.
- As shipped, Prompt 1 cannot reach "zero StyleCop errors". The options are:
  - **(a) SA1300 → `warning`.** This is my recommendation. New code is nudged toward PascalCase members, and the forced namespace doesn't block the build.
  - **(b) SA1300 → `none`.**
- I did not make this change myself because it relaxes a rule CLAUDE.md §7.2 deliberately put in the error tier.

**Remaining warnings on existing files (non-blocking):** SA1208/SA1209 for the `using SmartEnum = …` alias placed first (20), SA1133 (5), SA1507 (3), SA1122 (1), and SA1503 (1, `TimeSchedule.cs:45`). The new sample entity, with the alias placed after the `System` usings, raised none of these.

---

## Changes made in this pass

- **`DISCOVERY_NOTES.md`:** this file.
- **`CLAUDE.md`:**
  - §2.2: Module decision is undecided; use `HR` until decided.
  - §2.8: multi-tenancy is still undetermined; listed what was confirmed.
  - §5: no real command values exist.
  - §7.2: SA1309 resolved; SA1300 decision flagged.
  - §7.4: snippet and comment now include SA1312.
- **`.editorconfig`:**
  - SA1309 changed from `warning` to `none`, with a comment explaining why.
  - Pre-existing-files override now includes `SA1312 = none`.
  - Glob comment updated to say it was verified.
- **Not touched:** `PROMPT_PLAN.md`, `stylecop.json`, and all `robeem/DataModel/*.cs` files.

## Blocking before Prompt 1

Nothing. All three items listed here on 2026-10-03 were resolved on 2026-10-04 (see "Decisions after discovery" at the top):

1. The host solution will not be provided; the solution is standalone.
2. SA1300 has a brand-name suppressor.
3. Attendance entities use `Module.Attendance`.
