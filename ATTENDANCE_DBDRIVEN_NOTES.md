# Database-driven catalog: notes

Providers, file formats, device types and the names of the transports used to be partly code. They are now tenant data, managed by `IntegrationAdministrator` on the Integrations screens. This file records what was decided, what differs from the plan that was approved, and what is still open.

## Decisions (from the plan the product owner approved)

- **Providers:** create, edit and deactivate (never delete) at `api/attendance/providers` and the *Providers* screen. A provider is *usable* when it is active and has a default file format or a code adapter registered under its key (`ProviderUsability`, `IAttendanceAdapterCatalog`). The old rule "seeded active only if its adapter is registered" and its test are gone.
- **Built-in layouts as rows:** the generic CSV and ZKTeco readers and interpreters, and the `IAttendanceFileReader` marker, were deleted. Their layouts are the seeded file formats *Generic CSV* and *ZKTeco attendance export*. Only `SupremaBiostar*` (HTTPS pull) and `GenericWebhook*` (push) remain as code.
- **Device types:** the enum became the table `AttendanceDeviceType` (same name); `AttendanceDevice.DeviceTypeId`. Managed at `api/attendance/device-types`.
- **Transports:** `AttendanceTransportDefinition` holds a display name, `IsPull`, `UsesFileFormat` and `IsActive` for each of the six enum members, and `AttendanceProviderTransport` replaces the comma-separated list. Only the name and `IsActive` can be edited. File upload is implicit for every provider and can't be switched off.
- **Seeding:** idempotent, never overwrites. Existing integrations of the zkteco and generic-csv providers were pointed at the seeded formats.
- **Permissions:** `IntegrationAdministrator`; deactivate only.

## Differences from the plan

1. **The old text columns stay for one release.** The plan said the data would be converted by the migration. No PostgreSQL server was available to run SQL in a migration, so `AddDatabaseDrivenCatalog` only adds tables and columns, and the conversion is C# in the seeder (testable on SQLite). `AttendanceProvider.SupportedTransports` and `AttendanceDevice.DeviceType` keep their column names as `LegacySupportedTransports` / `LegacyDeviceType`; the seeder reads them once and clears them. **Closed:** migration `DropLegacyCatalogColumns` moves what the seeder never moved and drops both columns (CLAUDE.md §6).
2. **Linking existing integrations is done by the seeder, not the migration** (the plan's own note: the formats do not exist when the migration runs). It happens only in the run that creates the built-in formats, so a later choice by an administrator is kept. This is why `AttendanceDevice` and `AttendanceIntegration` now carry `ISeedData`.
3. **No file-extension field on a format** (the plan listed one). `FileKind` already decides the extensions. The consequence: ZKTeco's upload now also accepts `.txt` (it listed `.xls .xlsx .csv` before) and generic CSV is `.csv .txt .xls .xlsx`.
4. **The device-type entity is `AttendanceDeviceType`**, not `AttendanceDeviceTypeDefinition`, because the enum of that name was deleted.
5. **The webhook interpreter keeps its own ISO timestamp and label maps** (the plan's proposal), copied from the deleted generic CSV interpreter.

## Behaviour that changed

- **An upload for an integration with no file format fails the batch** with "has no file format ... Choose a file format for the integration", and its upload target says so. Before, a provider with no file reader (BioStar, the webhook) given manual upload was read with the generic CSV layout. Now such an integration needs a format chosen, or a default format on its provider.
- **Header matching for a file format.** A column setting may list several headers separated by `|` (new). A single header the file does not have is still an error; of several, the file may have any number, even none, and the field is then empty. The first non-empty value wins. Generic CSV picks by the order of the alias list, where the old code picked by the order of the file's columns; only a file with two alias columns could notice.
- **Seeded provider display names and manufacturer fields are no longer repaired.** The old seeder renamed legacy names and filled empty manufacturer/website fields on every start; those one-off repairs were dropped, so an administrator's blank stays blank.
- **Excel date cells** are still rendered by shared code (`TabularFile`) as `dd-MMM-yy HH:mm:ss`, a ZKTeco-style text, for every format. The seeded ZKTeco patterns cover it. A custom format reading an Excel date cell must list that pattern (unchanged, and still open: it could become a format setting).

## Open points

- Drop the two legacy columns (above).
- The PostgreSQL tests were run against a local server: all 7 pass, including a new one that seeds a database holding the old text columns (`TheSeed_MovesTheOldTextColumnsAndLinksTheBuiltInFormats_OnPostgres`). Two older tests in that file had their own bugs, fixed. Still not run: `AddDatabaseDrivenCatalog` applied over a database that already holds rows (it only adds nullable or defaulted columns and new tables).
- `UsesFileFormat` and `IsPull` are read by the screens only; the Api still decides by the enum (`FileUpload`, and a polling schedule only for pulling transports). They are seeded, read-only data.
- `EveryActionOfTheApi_NamesItsRoles_ExceptTheTenantPicker` fails in the current tree and did before this change: the anonymous `AttendanceWebhookController.Receive` is not in its exclusion list. Not touched here.
- Delimiters, file kinds and the biometric word list are still code (not asked).
- Time-zone suggestions on the device form and auto-import of devices (`RetrieveDevicesAsync`) are unchanged (not asked).
