# Prime tenant catalog: deviations and open points

Implements `PRIME_TENANT_PLAN.md` (2026-11-01). The decision and its rules are in `CLAUDE.md` §2.8; this file records where the build differs from the plan, what was assumed, and what is left.

## Decisions taken with the product owner

- The catalog is read **live from the prime tenant's database**. Tenant tables refer to it by key, with no foreign key.
- A new role, `PlatformAdministrator`, manages the catalog, in the prime tenant only.
- Ordinary tenants get **payload formats, read-only, with example files to download**. No screen for vendors, connectors, transports or device types.

## Deviations from the plan

1. **Every change to how a file is read is a new version.** The plan kept the old rule (a new version only once a batch used the format). The catalog cannot see other tenants' batches, so there is nothing to ask. A description or switching a format off is still made in place. Cost: a format being worked on collects versions; a draft is best made inactive until it is right.
2. **A payload format's name is its key and cannot change** (it could before). Same for a device type's name. A rename is refused with a message; the way out is a new name and deactivating the old one.
3. **A device's type name is matched ignoring case** and kept as the catalog spells it (the old id lookup behaved so in effect).
4. **`ConnectorOption.Id` and `PayloadFormatOption.Id` stay** in the contracts (the catalog's own screens still use ids to edit rows); tenant forms use `Key` and `Name`.
5. **Preview** (trying a sample file on a format) is platform-only, as assumed. Tenants download an example instead.
6. **`PlatformAdministrator` is also in `ActorProtocol.Roles`** (Contracts), so the Development "act as" picker and the stand-in user have it.
7. **Dev layout:** a third tenant `prime` (database `robeem_prime`) is appended to `appsettings.Development.json`, so the user-secrets indexes of `demo` (0) and `acme` (1) are unchanged. Set `Tenancy:Tenants:2:ConnectionString` in user-secrets.
8. `ICatalogWriteAccess` checks the role **and** the tenant; `CatalogDbContext` checks the tenant on every save. The Api answers a refusal as 403 with `catalog_read_only` through `CatalogReadOnlyFilterAttribute`.

## Not atomic

A catalog change and its audit entry are saved by two contexts. The catalog is saved first; if the second save fails, the change stands without its audit entry. The audit entry is the request tenant's own (the prime tenant's `AttendanceAuditLog`).

## Migrations: read before upgrading a database

- **`MoveCatalogToPrime` (tenant databases) is destructive and irreversible.** It first copies every id reference to a key (an integration's connector and format, a device's type, a batch's format name and version), then drops the seven catalog tables. Anything a tenant had added to its own catalog (a custom vendor, connector or payload format, a renamed transport) is **lost**. Integrations that pointed at such a connector keep its key but find nothing in the prime catalog: re-create it there under the same key before upgrading, or those integrations cannot be read.
- **`InitialCatalog` (prime database)** creates the `Catalog` schema. The catalog is then seeded with the built-in offering. If the prime tenant's own database held a catalog of its own, it is dropped by the tenant migration, like any other's.
- Order at startup: tenants are migrated and seeded, then `CatalogDatabaseInitializer` runs once. In production, where `Database:InitializeOnStartup` is false, run both scripts (`DEPLOYMENT_IIS.md` §7.1).

## Not done, and why

- **A cache for `ICatalogReader`.** A prime outage stops ingestion, normalization and the forms that list connectors. `ICatalogReader` returns records so a short-TTL, stale-on-error cache is a change in one class; it was left out as scope.
- **A read-only database user for the catalog connection** in non-prime scopes (defence below the application's checks).
- **Usage counts** on catalog lists are gone (they would need other databases).
- **The Web asks the Api which tenant it is on twice per page** (the access filter and the navbar's tenant label each call `GET api/tenants/current`). Harmless; the label could share `ICurrentTenantInfo`.
- **PostgreSQL tests were written but not run** (no connection string in the session). Run them before relying on the migrations:
  `$env:ROBEEM_TEST_POSTGRES = "Host=localhost;Username=postgres;Password=<pwd>"; dotnet test robeem.attendance.slnx --filter "FullyQualifiedName~PostgresMigrationTests"`.
  `MoveCatalogToPrime` in particular is hand-written SQL that SQLite cannot exercise.
- **No end-to-end run of the Api and Web** against real databases was done either (the default run is 1,342 tests on SQLite).

## Verification at the time of writing

- `dotnet build robeem.attendance.slnx`: 0 errors, 0 warnings.
- `dotnet test robeem.attendance.slnx`: 1,342 passed, 11 skipped (the opt-in PostgreSQL tests), 0 failed.
- `dotnet format robeem.attendance.slnx --verify-no-changes`: clean.
