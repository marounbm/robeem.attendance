# Integration restructure: vendor → connector → payload format, flexible timestamps, per-device connections

Status: **approved 2026-10-24, implementation started** (Stage 0). This file is the working copy of the approved plan; CLAUDE.md is updated stage by stage as each one lands.

## Context

The integration model has grown by accretion, and three new needs do not fit it:

- **A. Protocol layer.** A vendor ships several products and protocols (ZKTeco: a desktop export, a REST API, a push protocol). Today a *provider* points straight at one default *file format*, so one vendor can't have several protocols, and nothing records which payload kinds (CSV, JSON, XML) a protocol supports or whether it is encrypted.
- **B. Timestamps.** A format can read one timestamp column, or a date column and a time column. The two parts are joined with a space and parsed with the *same* patterns, so the patterns must describe the combined text. There are no separate date and time patterns, no culture (Arabic or French month names fail), no ambiguity warning, and Excel date-only or time-only cells are untested.
- **C. Transport and authentication.** Only the integration has a transport; a device has none. SFTP, Database and DeviceSdk exist as enum members and seed rows only. Credentials are one opaque secret string, and nothing defines the settings or authentication each transport needs.

Existing naming problems found: `Provider` means both the vendor row and the `IAttendanceProvider` code adapter; `ProviderKey` is the adapter key even for file-only vendors; `Legacy*` columns were never dropped; `SupportsWebhooks` duplicates the Webhook transport; `DefaultFileFormat` (provider) vs `FileFormat` (integration); "format", "layout" and "mapping guide" are used interchangeably; "file format" is wrong once JSON/XML exist.

**Decisions taken with the product owner:** full rename including Provider → Vendor; JSON and XML readers **and** decryption are built in this work; every transport is modelled, validated and has a test-connection, but no new fetching adapter is built (SFTP pull is the next piece of work); the per-device connection dropdown ships now.

## Design changes versus the first draft (from the design review)

1. The new layer is **`AttendanceConnectorDefinition`**, not "profile": `Profile` is the HR employee entity (CLAUDE.md §2.4) and the generic indexer resolves navigations by name.
2. Supported payload kinds are **derived** from a connector's active formats, not stored twice. Push/pull is derived from its transports; `SupportsWebhooks` goes away. Only `SupportsIncrementalSync` stays.
3. A device points **to** a connection (`Device.ConnectionId`), not the other way round, so twenty clocks can share one endpoint. The integration's default connection is marked `IsDefault` (filtered unique index); no `Integration.DefaultConnectionId`, which would create an insert cycle.
4. A payload format may be shared by many connectors (`ConnectorId` nullable; "Generic CSV" is used across vendors). The connector's default is `Format.IsDefault`, not a `Connector.DefaultFormatId` FK (also a cycle).
5. **Sync state stays on the integration** (cursor, last success, health, Polly breaker) with one pulling connection per integration enforced. `ConnectionId` is recorded on each batch so moving state to the connection later is cheap.
6. **Separate date/time parsing uses the cross-product of patterns** (`datePattern + " " + timePattern`, capped at 10×10), so the stored raw timestamp text stays re-parseable by normalization and legacy formats need no data conversion. The mode is derived, not stored.
7. **Formats are versioned.** Normalization re-parses `TransactionTimestampRaw` with the integration's *current* format, so editing a format silently changes how retried rows are read. Parse-affecting edits create a new version; batches record the format version used.
8. Secrets: single-value authentication stays a raw string (HMAC key, API key, bearer token), so existing webhook secrets keep working; multi-part authentication is a JSON object. The reference value is copied unchanged, so no secret-store key changes.

## Final naming

| Current | New | Notes |
|---|---|---|
| `AttendanceProvider`, `ProviderKey`, `ProviderId` | `AttendanceVendor`, `VendorKey`, `VendorId` | Stable company data only: key, name, website, active. `Manufacturer` is dropped (always equal to the name). Values of `VendorKey` are unchanged: they are also the stored `SourceSystem` of raw punches. Routes `providers` → `vendors`; screens "Vendors". |
| `IAttendanceProvider` (code adapter) | `IAttendanceAdapter` | Resolves the "provider" homonym; matches `IAttendanceAdapterCatalog`. |
| (new) | `AttendanceConnectorDefinition` | One per vendor software/protocol. `ConnectorKey`, `AdapterKey` (nullable: the keyed adapter and interpreter pair; null = format-driven, data only). |
| `AttendanceProviderTransport` | `AttendanceConnectorTransport` | Join moves to the connector. |
| `AttendanceFileFormat` / `...EventCode` | `AttendancePayloadFormat` / `AttendancePayloadEventCode` | `FileKind` → `PayloadKind` (adds `Json`, `Xml`); `FileFormatId` → `PayloadFormatId`. |
| (new) | `AttendanceConnection` | A configured endpoint and its authentication, belonging to an integration; devices may point to one. |
| `ConnectionConfigRef`, `PollingScheduleCron` | `SecretRef`, `PollingSchedule` | Move to the connection. |
| `AttendanceTransportDefinition` | unchanged | A tenant's names for the transport enum. |
| `AttendanceProcessingBatch`, `AttendanceUploadedFile`, raw/normalized transaction | unchanged | Accurate as named. |
| `Provider.DefaultFileFormatId`, `SupportsWebhooks`, `LegacySupportedTransports`, `AttendanceDevice.LegacyDeviceType` | dropped | In a later "contract" migration. |

## Target model

All entities: `ITenantScoped`, schema `Attendance`, FKs Restrict, composite uniques start with `TenantId`, enums stored as strings, `Title` overridden. Catalog entities carry `ISeedData`.

- **`AttendanceVendor`** (`Entity`, ISeedData): `VendorKey` varchar(100) unique, `DisplayName` 255, `WebsiteUrl` 255, `IsActive`.
- **`AttendanceConnectorDefinition`** (`Entity`, ISeedData): `VendorId`, `ConnectorKey` varchar(100) unique, `DisplayName`, `Description` 1000, `AdapterKey` varchar(100)? indexed, `SupportsIncrementalSync`, `EncryptionKind` (`None, Pgp, Aes, PasswordProtected`), `IsActive`; collections `Transports`, `Formats`.
- **`AttendanceConnectorTransport`** (`SystemEntity`, ISeedData): `ConnectorId`, `TransportId`; unique per pair. File upload stays implicit.
- **`AttendancePayloadFormat`** (the old format): `ConnectorId` int? (null = shared), `PayloadKind`, `IsDefault` (filtered unique per connector), `Version`, `SupersedesId` (self FK), unique `(Tenant, Name, Version)`. Field mapping unchanged. Timestamp spec: `TimestampColumn` + `TimestampFormats` (single), or `DateColumn` + `DateFormats` and `TimeColumn` + `TimeFormats` (separate), plus `TimestampCulture` varchar(16) (empty = invariant). `RecordPath` varchar(255) for JSON/XML (the path to the repeating record; column fields hold paths relative to it).
- **`AttendanceIntegration`**: `ConnectorId` required (replaces `ProviderId`); `PayloadFormatId` (renamed). Keeps name, `DefaultDeviceId`, `TimeZoneId`, `Status`, `HealthStatus`, `SyncCursor`, `LastSuccessfulSyncAt`. `Transport`, `ConnectionConfigRef`, `PollingScheduleCron` move to the connection.
- **`AttendanceConnection`** (`Entity`): `IntegrationId`, `Name` (unique per integration), `IsDefault` (filtered unique per integration), `Transport`, `AuthMethod` (`None, UsernamePassword, SshKey, ApiKey, BearerToken, OAuth2ClientCredentials, SessionLogin, HmacSignature, AgentToken`), `Settings` (jsonb on Npgsql, text on SQLite; non-secret), `SecretRef` (transport authentication), `PayloadKeyRef` (decryption key, separate from transport authentication), `PollingSchedule`, `IsActive`, `LastTestedAt`, `LastTestSucceeded`, `LastTestMessage` (fixed messages only, never exception text).
- **`AttendanceDevice`**: `ConnectionId` int? (null = the integration's default). Rule: the connection belongs to the same integration. **`AttendanceProcessingBatch`**: `ConnectionId?`, `PayloadFormatId?`.
- **In code, not tables:** `ITransportDescriptor` per transport (fields, which are secret, allowed auth methods, secret shape); a Contracts DTO at `GET api/attendance/transports/descriptors` so the Web renders connection forms dynamically; `TimestampPatternCatalog` (static list of pattern, example, kind; stored values are the pattern strings, never catalog ids).

Descriptors: **Sftp** {host, port, remotePath, filePattern, archivePath, hostKeyFingerprint required; Password | SshKey}; **RestApi** {baseUrl, paths; ApiKey | UsernamePassword | BearerToken | OAuth2ClientCredentials | SessionLogin}; **Webhook** (inbound) {HmacSignature | ApiKey | BearerToken}; **Database** {engine, host, port, database, query; UsernamePassword}; **DeviceSdk** {agentId; AgentToken}; **FileUpload** {no transport authentication}.

## Structure best practices (to be recorded in CLAUDE.md)

1. **Three tiers.** Catalog (vendor, connector, transport definition, payload format): seeded, administered by IntegrationAdministrator, deactivated and never deleted. Configuration (integration, connection, device): tenant-owned. Runtime state (cursor, health, test result, batches, raw): written by jobs only.
2. **Effective configuration, most specific wins**, resolved in one `EffectiveConfigResolver` (replaces `FileFormatResolution` and `ProviderUsability`): connection = `device.Connection ?? integration default`; format = `integration.PayloadFormat ?? connector default`; adapter = `connector.AdapterKey`. A connector is usable when active, has an adapter or a default format, and a decryptor is registered for its `EncryptionKind`.
3. **Immutability and versioning.** Formats are versioned; raw rows are never touched; every edit is audited.
4. **Secrets.** Only references are stored; `Settings` rejects any field the descriptor flags secret; secrets are resolved per call inside the Polly pipeline and never logged; decryption keys come from `PayloadKeyRef`.
5. **Validation by descriptor.** Undeclared setting keys and authentication methods the transport doesn't allow are refused; SFTP requires a host-key fingerprint.
6. **Invariant 9 extends:** a new vendor of exported files = a vendor, a connector with no `AdapterKey`, and a format row; no code. Add a test in the style of `NewVendorAdditivityTests`.
7. **Naming:** `*Definition` for catalog rows, `*Key` for stable slugs, `*Ref` for secret references, `Is*` for flags, nouns for entities; no `Legacy*` outside a migration window.

## Stages

Each stage ends green (`dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`) and is committed separately. Stage 1 is independent of the rest.

**Stage 0: safety.** Rename `IAttendanceProvider` → `IAttendanceAdapter` (code only, ~13 src + 5 test files). Add a shared test data builder (`AttendanceTestData`): 12 test files construct `AttendanceProvider` and 17 construct `AttendanceIntegration`, so the later entity changes touch one file. Add characterization tests for joined date+time parsing and ZKTeco Excel typed cells (`ZktecoXlsUploadTests`).

**Stage 1: timestamp spec (B).** Migration `AddTimestampSpec` (three additive columns, no data step). `CustomFormatInterpreter`: pattern sets per mode, culture-aware `TryParseExact`, cross-product for separate mode, `zzz`/`'Z'` still allowed. `TabularFile` exposes typed cells; in separate mode a date cell contributes its date and a time-only cell (1899-12-30 base or fraction) its time of day, rendered in one canonical form pinned by a test; the text path stays byte-identical for ZKTeco. `AttendanceFileFormatService.Validate`: date patterns need y/M/d, time patterns H or h, every pattern round-trips, warn when a d/M-swapped pair is selected ("first listed wins"). The preview shows every pattern that matches each sample row and the value it gives. UI: single/separate switch, catalog multi-selects, custom textarea, culture picker. Representative files: `AttendanceFileFormat.cs`, `FileFormatInput.cs`, `AttendanceFileFormatService.cs`, `FileFormatLayout.cs`, `CustomFormatFileReader.cs`, `CustomFormatInterpreter.cs`, `TabularFile.cs`, `Views/FileFormats/_FileFormatForm.cshtml`, new `TimestampPatternCatalog.cs`.

**Stage 2a: mechanical rename.** Provider → Vendor and FileFormat → PayloadFormat, with no behaviour change, as `RenameTable`/`RenameColumn`/`RenameIndex` (including FK and index names) in one migration, plus routes, DTOs, screens, tests. Done first, alone, so the diff is reviewable. Churn: `ProviderKey` 27 src + 24 tests, `AttendanceProvider` 15 + 12, `AttendanceFileFormat` 16 + 4.

**Stage 2b: connector layer (A).** New connector entities and configurations (`Persistence/Configurations/Attendance`), `AttendanceConnectorService`, `api/attendance/connectors`, Web connector screens next to *Vendors*. `Integration.ConnectorId` replaces `VendorId`; the service enforces `connector.VendorId` consistency while both exist. Update `IngestionService` (upload targets, `ReadUploadAsync`), `NormalizationService.GetIntegrationAsync`, `AttendanceIntegrationService`, `AttendanceVendorService`, `WebhookReceiver`, `IntegrationSyncService`, `AttendanceSeedData`/`Contributor` (seed one connector per existing vendor, keyed by `ConnectorKey = VendorKey`, never overwriting an administrator's edit). Migration `AddConnectorDefinitions` (expand): create tables; `INSERT ... SELECT` one connector per vendor with `AdapterKey` = `VendorKey` where the vendor has no default format; copy transports (adding Webhook where `SupportsWebhooks`); backfill and then require `Integration.ConnectorId`; set `Format.ConnectorId`/`IsDefault` from `DefaultFileFormatId` (a format used by several connectors stays shared). A later contract migration drops the old provider columns, `ProviderTransport` and `Legacy*`. The seeder also catches up idempotently (SQLite tests don't run migration SQL).

**Stage 3: JSON, XML and decryption.** (Done; see CLAUDE.md §6. As built: JSON/XML are flattened to a table behind `ICustomFormatFileReader`, `PayloadKeyRef` is on the integration until Stage 4 adds the connection, and the zip decryptor reads password-protected zip archives only.)
- JSON: a small in-house path evaluator (dot paths, `[*]` over arrays; documented subset, no new dependency). XML: built-in `System.Xml.XPath` with DTD processing prohibited, no external resolver, and size limits. Both go through `RawPayloadSanitizer` (allowlist, invariant 10) and use the same event-code and timestamp machinery; `ICustomFormatFileReader` is extended behind the same interface.
- Decryption: `IPayloadDecryptor` per `EncryptionKind` (interface in Application, implementations in Infrastructure like the readers): PGP (BouncyCastle), AES (key and IV from the secret), password-protected zip/Office (SharpZipLib; ExcelDataReader's password option). Key material comes from `PayloadKeyRef`, is used for one call and never logged. Decryption runs before reading, to a temp file that is deleted. New NuGet packages: `BouncyCastle.Cryptography`, `SharpZipLib`.
- Validation lifts the temporary "kind not supported" block. Tests: round-trip fixtures generated in the tests (PGP key pair, AES, zip), XXE and oversize rejection, JSON/XML sample files per vendor-style format.

**Stage 4: connections and per-device transport (C).** Domain `AttendanceConnection`, `Device.ConnectionId`, batch columns; descriptors and `ConnectionSettingsValidator`; `AttendanceConnectionService`; `api/attendance/connections` with `POST {id}/test` (IntegrationAdministrator, audited, timeouts, never returns response bodies); connection section on the integration form (descriptor-driven); a **Connection** dropdown on the device form and a column on the Devices grid. Webhook: add `POST api/attendance/webhooks/{tenantId}/c/{connectionId}`, keep the old `{integrationId}` route mapped to the default Webhook connection; `WebhookReceiver` checks `connection.Transport == Webhook` and `HmacSignature` instead of `SupportsWebhooks`. BioStar adapter reads `baseUrl` from `Settings` first, falling back to the secret. `IntegrationSyncService` reads the connection's schedule. Migration `AddConnections`: one default connection per integration copying `Transport`, `ConnectionConfigRef` → `SecretRef`, `PollingScheduleCron` → `PollingSchedule`; `AuthMethod` derived (BioStar REST → `SessionLogin`, Webhook → `HmacSignature`, else `None`). Sftp/Database/DeviceSdk answer test-connection with "no adapter available yet".

**Docs.** Update CLAUDE.md (§4 table and names, §6 roadmap entries per stage with their rules), `DATABASE.md`, `TEST_COVERAGE.md`.

## Verification

- Every stage: `dotnet build robeem.attendance.slnx` (zero warnings), `dotnet test robeem.attendance.slnx`, `dotnet format robeem.attendance.slnx --verify-no-changes`. The Api and Web must be stopped (they lock the output folders); `ApiAuthorizationTests.EveryActionOfTheApi_NamesItsRoles...` must name roles on every new endpoint.
- Migrations: run `PostgresMigrationTests` (set `ROBEEM_TEST_POSTGRES`) including a new test that seeds the *old* shape, migrates, and asserts the backfill (connector per vendor, default connection per integration, formats' `ConnectorId`/`IsDefault`).
- Stage 1: unit tests for both modes, both ambiguous orders, culture, typed Excel cells, the cross-product cap; existing `CustomFileFormatTests`, `ZktecoFormatTests` and `ZktecoXlsUploadTests` stay green unchanged.
- Stage 2: resolver tests (usability with encryption, format precedence); a new-vendor-is-data test; upload and normalization of the three seeded file vendors end to end (`AttendanceEndToEndTests`).
- Stage 3: encrypted-file and JSON/XML round trips through ingestion → normalization.
- Stage 4: each descriptor (undeclared and secret keys rejected), device → default connection resolution, both webhook routes, test-connection audit entry, no secret in any log or message (extend the existing `ToString`/error-message tests).
- Manual, after restarting the Api (Development migrates demo/acme at startup): open Vendors, Connectors, Payload formats; create an integration with a connector and a default connection; upload a CSV with separate date and time columns using two candidate formats and check the preview; set a device's connection; run test-connection and confirm the result and the audit entry.

## Risks

- The rename is large and mechanical; doing it alone (stage 2a) keeps it reviewable. `VendorKey` values must never be rewritten (stored in raw rows).
- Migration SQL is not exercised by the SQLite tests; mitigated by the Postgres test and the idempotent seeder catch-up.
- Retroactive re-parse on format edits: closed by format versioning and `PayloadFormatId` on batches.
- Two new NuGet dependencies for decryption, and untested vendor encryption conventions (AES mode and IV layout vary by vendor): the AES parameters are kept in the secret so a vendor variant needs configuration, not code, wherever possible.
- `TwoDigitYearMax` and non-Latin digits are not handled; noted in the UI.
- SSRF: administrator-entered hosts and URLs are allowed (BioStar is on-premises), limited to IntegrationAdministrator, audited, with timeouts and no response bodies returned.
- Out of scope, next: the SFTP pull adapter, Database and DeviceSdk adapters, moving sync state to the connection (when two pulling connections per integration are needed), webhook IP allowlist.
