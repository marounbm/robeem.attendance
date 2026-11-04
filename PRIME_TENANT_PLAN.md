# Move the Attendance catalog to a prime tenant

Status: implemented (2026-11-01); where the build differs from this plan, and what is left, is in `ATTENDANCE_PRIME_NOTES.md`. The rules it settled are in `CLAUDE.md` §2.8. Follows the style of `INTEGRATION_RESTRUCTURE_PLAN.md`. When a stage is finished, add a line for it to the roadmap in `CLAUDE.md` and record deviations in `ATTENDANCE_PRIME_NOTES.md`.

## Context

robeem is a SaaS ERP. Modules must get shared, vendor-supplied data from a **prime tenant**. For Attendance, the catalog is robeem's offering to every customer: vendors, connectors, transports, device types and payload formats (with event codes). Today each tenant database holds its own copy, seeded and edited per tenant. The catalog belongs to the prime tenant instead:

- The prime tenant creates and manages it.
- Every other tenant reads it live, from the prime database.
- Ordinary tenants can *view* payload formats and *download sample files*. They cannot create or modify any catalog data.

Decisions already made with the product owner:

| Topic | Decision |
|---|---|
| Access | **Live read from the prime DB.** No catalog tables in tenant databases. Tenant tables refer to the catalog by key, not by foreign key. |
| Management role | New `PlatformAdministrator` role, valid only when the request's tenant is the prime tenant. |
| Ordinary tenant screens | Payload formats (read-only) plus sample download. Vendors, connectors, transports and device types get no tenant screen. |

Assumptions (to be confirmed):

1. The prime tenant is also an ordinary tenant (robeem's own company data, same `RobeemDbContext` schema). It still gets jobs and seeding like any other tenant. Its database additionally holds the catalog, in a separate `Catalog` schema.
2. Tenant forms still need the catalog as pick-lists: active connectors when making an integration, device types when registering a device. These are *reads* and need no new screen.
3. Existing catalog rows in tenant databases are **not** carried to the prime. Only the seeded catalog is re-seeded there. Custom vendors, connectors or formats a tenant made, and tenant-edited transport names, are dropped. References from integrations, devices and batches are converted to keys first.
4. Usage counts on catalog lists (`IntegrationCount`, `DeviceCount`, the per-transport connection counts) cannot be computed across tenants and are removed from those lists.
5. Preview (testing a file against a format) stays a prime-only action. Ordinary tenants get view and sample download, as asked.

## Design

### Prime tenant
- Config `Tenancy:PrimeTenantId` is required and must name a registered tenant. The registry validates this at startup, in `src/robeem.Infrastructure/Tenancy/TenantRegistry.cs`.
- `ITenantRegistry` gains `PrimeTenant`.
- A new `IPrimeTenantContext` in `src/robeem.Application/Tenancy` exposes `IsCurrentTenantPrime`.
- `CurrentTenantResponse` (Contracts/Tenancy) gains `IsPrime`, so the Web can show or hide platform screens. The Web may not reference inner layers, so this has to travel as a DTO field.

### Catalog persistence: second DbContext
- Add `SmartEnum.Module.Catalog`. The seven catalog entities move to `Schema = nameof(SmartEnum.Module.Catalog)`. They are `AttendanceVendor`, `AttendanceConnectorDefinition`, `AttendanceConnectorTransport`, `AttendanceTransportDefinition`, `AttendanceDeviceType`, `AttendancePayloadFormat` and `AttendancePayloadEventCode`.
- They **drop `ITenantScoped`**. This is a deliberate, documented exception to §2.8, because the data is platform-wide. Their unique indexes lose the `TenantId` prefix. They keep `ISeedData`.
- New `ICatalogDbContext` in `src/robeem.Application/Persistence`: the seven `DbSet`s, `ChangeTracker` and `SaveChangesAsync`.
- New `CatalogDbContext` in `src/robeem.Infrastructure/Persistence/Catalog`, always connected to `PrimeTenant.ConnectionString`:
  - No tenant filter and no `TenantId` column.
  - It reuses the Restrict-FK and `DateTime` mapping conventions of `RobeemDbContext`.
  - Its own migration set, `Persistence/Catalog/Migrations`, starting with `InitialCatalog`.
  - Its history table is `Catalog.__CatalogMigrationsHistory`. It must not collide with `RobeemDbContext`'s history table in the same prime database.
  - A design-time factory for `dotnet ef --context CatalogDbContext`.
- **Write guard.** `CatalogDbContext.SaveChanges*` throws `CatalogReadOnlyException` unless `IPrimeTenantContext.IsCurrentTenantPrime`. This is defence in depth below the role check and the service check. Optional hardening, to document and not build now: a read-only database user for the catalog connection in non-prime scopes.
- Remove the seven `DbSet`s from `IRobeemDbContext` and `RobeemDbContext`, along with their configurations. Tenant entities may then have no foreign key into the catalog. The compiler enforces most of this, and one test enforces the rest (see Verification).

### References by key (tenant tables)

| Today | New column(s) |
|---|---|
| `AttendanceIntegration.ConnectorId` and `Connector` | `ConnectorKey` (string, the immutable connector key) |
| `AttendanceIntegration.PayloadFormatId` and `PayloadFormat` | `PayloadFormatName`. The effective format is the **latest version of that name**. Creating a new version no longer repoints integrations, and the repointing code in `AttendancePayloadFormatService.CreateVersionAsync` goes away. |
| `AttendanceDevice.DeviceTypeId` and `DeviceType` | `DeviceTypeName`. Device type `Name` becomes immutable (it is the key). Display text can't change; deactivate instead. |
| `AttendanceProcessingBatch.PayloadFormatId` and `PayloadFormat` | `PayloadFormatName` plus `PayloadFormatVersion`. Normalization re-reads a batch by the exact version it recorded, which preserves the existing versioning rule. |
| `RawAttendanceTransaction.SourceSystem` | unchanged (`VendorKey`) |

### Reading the catalog from tenant code
- New `ICatalogReader` in `src/robeem.Application/Attendance/Catalog`. It returns plain records, not entities, so a cache can be added later:
  - `GetConnectorAsync(key)` and `ListConnectorsAsync(keys?)`. A `ConnectorInfo` carries the vendor key, vendor and connector active flags, `AdapterKey`, `SupportsIncrementalSync`, `EncryptionKind`, transports, default format name and `IsUsable` (it reuses `ConnectorUsability`).
  - `GetEffectiveFormatAsync(name)` returns the latest version, with event codes.
  - `GetFormatVersionAsync(name, version)`.
  - `ListDeviceTypesAsync`.
  - Active-transport lookups.
- Joins such as `Include(Connector).ThenInclude(Vendor)` become two steps: load the tenant rows, then resolve the keys through `ICatalogReader`. Filters such as `Connector.IsActive && Vendor.IsActive` become a catalog query for the active keys, then `Where(i => keys.Contains(i.ConnectorKey))`.
- Known cost: a prime outage stops resolution of connectors and formats. A short-TTL, stale-on-error cache inside `ICatalogReader` is the mitigation. It is not in scope now, but the record-returning API keeps it a local change.

Consumers to refactor (all in `src/robeem.Application/Attendance`):
- `IngestionService`
- `IntegrationSyncService`
- `NormalizationService`
- `AttendanceIntegrationService`, including its device methods
- `AttendanceConnectionService`
- `WebhookReceiver`
- `IntegrationHealthService`
- `RawBatchWriter`
- `PayloadFormatResolution`
- `ConnectorUsability`
- `PayloadFormatLayout` call sites

### Management (prime only)
- Catalog services (`AttendanceVendorService`, `AttendanceConnectorService`, `AttendanceTransportService`, `AttendanceDeviceTypeService`, `AttendancePayloadFormatService`) switch from `IRobeemDbContext` to `ICatalogDbContext`. Every mutating method first calls a new `ICatalogWriteAccess.EnsureCanWrite()` (prime tenant + current-user check).
- Audit entries are still written to the request tenant's `AttendanceAuditLog`, which is the prime tenant when an administrator writes the catalog. That is a second context in the same database. Save the catalog change first, then the audit entry. They are not atomic, and this is recorded in `ATTENDANCE_PRIME_NOTES.md`.
- Seeding splits:
  - New `ICatalogSeedContributor` and `CatalogSeedContributor` (moved from the catalog half of `AttendanceSeedContributor` and `AttendanceSeedData`). Idempotent, add-only. It runs **once**, against the prime database only, from a new `CatalogDatabaseInitializer` called after the tenant initializer in `Program.cs`. The `ISeedData` guard of `SeedDataRunner` applies to it too.
  - `AttendanceSeedContributor` keeps only the default `AttendancePolicy`. Its back-fill of unformatted upload integrations goes, because the migration handles existing rows and new integrations resolve through the connector's default format.

### Security
- Add `PlatformAdministrator` to `AttendanceRoles` and `SecurityClaims` docs. Add it to `All`, and add it to the Development user's roles and the stand-in actor list. Update the test that enforces roles on every action.
- Catalog write endpoints (POST and PUT on vendors, connectors, transports, device-types and payload-formats, plus payload-format `preview`): `[Authorize(Roles = PlatformAdministrator)]`. The service also rejects a non-prime tenant, with a ProblemDetails `code` of `catalog_read_only` (403).
- Catalog read endpoints are open to `IntegrationAdministrator` and `PlatformAdministrator`. This covers the existing list, options and input endpoints, plus a new `GET api/attendance/payload-formats/{id}/sample?kind=`. The new endpoint builds the example from the stored format with the existing `PayloadFormatSampleBuilder`. The current POST `sample` for unsaved forms stays platform-only.
- Vendor, connector, transport and device-type list endpoints become platform-only, since no tenant screen uses them. The pick-list endpoints under `integrations/connectors` and `device-types/options` stay tenant-readable.

### Web
- `CurrentTenantResponse.IsPrime` flows into a small per-request `ICurrentTenantInfo` service in the Web.
- `NavigationMenu`:
  - The section "Integration setup" splits into **Platform catalog** (Vendors, Connectors, Transports, Device types, Payload formats). It shows only when the tenant is prime and the user is a `PlatformAdministrator`. This is a new `NavItem` flag, enforced by the same `NavigationMenu.AccessTo` that `ActorAccessFilter` uses.
  - Ordinary tenants get one item, **Payload formats**, for `IntegrationAdministrator`. It is a read-only list plus a detail page.
- `PayloadFormatsController` gains read-only `Details` and `Sample` (GET) actions for tenants. A read-only variant of `_PayloadFormatForm` has disabled fields, a *Download example* button (CSV, JSON, XML) and no Save. Create, Edit, Preview and the other catalog controllers stay prime-only. A `catalog_read_only` Api answer renders the existing access-denied page.
- Integration and device forms: connector and device-type pick-lists use keys. `IntegrationInput` gets `ConnectorKey`/`PayloadFormatName`, `DeviceInput` gets `DeviceTypeName`, and list items follow.

### Migrations and config
- **`RobeemDbContext` migration `MoveCatalogToPrime`**, hand-written SQL (the scaffold would be wrong):
  - It adds the key columns and fills them from the existing joins: `ConnectorKey`, `PayloadFormatName`, `DeviceTypeName`, and the batch's name and version.
  - It then drops the FK constraints, the indexes and the old id columns, and drops the seven catalog tables.
  - It is irreversible, and `Down` says so.
  - This is destructive for custom catalog rows (assumption 3). Call it out in the commit message and in `ATTENDANCE_PRIME_NOTES.md`.
- **`CatalogDbContext` migration `InitialCatalog`** creates the catalog schema in the prime database.
- Config: `Tenancy:PrimeTenantId`. The Development tenants gain a dedicated `prime` tenant on `robeem_prime`, with its connection string in user-secrets index 2. `DEPLOYMENT_IIS.md` documents the environment variable, `Tenancy__PrimeTenantId`.
- `TenantsController.List` is unchanged. The prime tenant appears in the picker like any other.

## Critical files
- Domain: `src/robeem.Domain/Core/SmartEnum.cs`, `src/robeem.Domain/Attendance/Attendance{Vendor,ConnectorDefinition,ConnectorTransport,TransportDefinition,DeviceType,PayloadFormat,PayloadEventCode}.cs`, `AttendanceIntegration.cs`, `AttendanceDevice.cs`, `AttendanceProcessingBatch.cs`
- Application: `Persistence/IRobeemDbContext.cs` (+ new `ICatalogDbContext`), `Tenancy/*`, `Attendance/Catalog/*` (new), the catalog and consumer services listed above, `Seeding/*`, `ApplicationServiceCollectionExtensions.cs`
- Infrastructure: `Persistence/RobeemDbContext.cs`, new `Persistence/Catalog/*`, `Persistence/Configurations/Attendance/*` (the catalog configs move), `Tenancy/TenantRegistry.cs`, `TenantDatabaseInitializer.cs`, `InfrastructureServiceCollectionExtensions.cs`, `Seeding/SeedDataRunner.cs`
- Api: the five catalog controllers, `Security/AttendanceRoles.cs`, `Program.cs`, `appsettings*.json`
- Contracts: the Integration, Device and Tenancy DTOs listed above
- Web: `Navigation/NavigationMenu.cs`, `PayloadFormatsController`, the integration and device forms, `Views/PayloadFormats/*`

## Order of work
1. **Foundation.** Prime tenant config and registry; `Module.Catalog`; `ICatalogDbContext` and `CatalogDbContext` with `InitialCatalog`; the write guard and `PlatformAdministrator`; `CatalogDatabaseInitializer` and `CatalogSeedContributor`.
2. **Re-key and read path.** `ICatalogReader`; swap the tenant entities to key columns; `MoveCatalogToPrime`; refactor the consumers one by one.
3. **Management and API.** Switch the catalog services to `ICatalogDbContext`; re-role the controllers; add the read-only sample endpoint.
4. **Web.** Menu split, read-only payload-format pages, integration and device forms by key.
5. **Tests and docs.** Update `CLAUDE.md` (§2.8 prime tenant, §4 table, §8 rules, roadmap entry), add `ATTENDANCE_PRIME_NOTES.md`, update `TEST_COVERAGE.md` and `DEPLOYMENT_IIS.md`.

Each step ends with a green build, so work can stop between steps.

## Verification
- `dotnet build robeem.attendance.slnx` has zero errors and warnings. Run `dotnet format robeem.attendance.slnx --verify-no-changes` as well.
- `dotnet test robeem.attendance.slnx`. `TestDatabase` gets a second in-memory SQLite connection for the catalog, with a `SeedAttendanceCatalog()` that seeds it. Existing service tests move onto it. Test files that construct catalog-reading services directly will need the extra constructor argument.
- New tests:
  - Writing through `CatalogDbContext` from a non-prime tenant throws `CatalogReadOnlyException`, and works from the prime tenant.
  - Catalog write endpoints return 403 for `IntegrationAdministrator` and succeed for `PlatformAdministrator` in the prime tenant. The same role in an ordinary tenant gets `catalog_read_only`.
  - An ordinary tenant can list payload formats and download a CSV, JSON and XML example.
  - Creating a new format version needs no change to tenant data. An integration's effective format becomes the new version, and an old batch still re-reads with its recorded version.
  - A model test: no `RobeemDbContext` entity has a navigation or FK to a catalog type.
  - Web: the menu shows the platform section only for prime plus `PlatformAdministrator`.
- Opt-in PostgreSQL run (`ROBEEM_TEST_POSTGRES`): `PostgresMigrationTests` gets a case where a tenant database with a populated catalog, integrations, devices and batches goes through `MoveCatalogToPrime`, and the key columns end up correct. A second case applies `InitialCatalog` beside the `RobeemDbContext` migrations in one database.
- Manual: create the three Development databases and set user-secrets. Run the Api and Web as `acme` and check that the formats list is read-only, the example downloads, and `POST` answers 403. Switch to `prime` and check that creating a format makes it appear in `acme` immediately. Upload a file in `acme` and confirm it is ingested and normalized by the prime-owned format.
