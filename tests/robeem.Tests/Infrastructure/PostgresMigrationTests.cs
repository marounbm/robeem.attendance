using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using robeem.Application.Attendance;
using robeem.Application.Attendance.Seeding;
using robeem.Application.HR.Seeding;
using robeem.Application.Seeding;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;
using robeem.Infrastructure.Persistence.Catalog;
using robeem.Infrastructure.Seeding;
using robeem.Infrastructure.Tenancy;

namespace robeem.Tests.Infrastructure;

/// <summary>
/// The real database engine (CLAUDE.md §2.8): the migrations applied to an empty PostgreSQL
/// database, and the model's special cases (UTC instants, local times, filtered unique indexes)
/// exercised on Npgsql, which SQLite cannot do. Needs a server: set <c>ROBEEM_TEST_POSTGRES</c> to
/// a connection string of a user who may create databases (any database name); the tests then
/// create and drop a throwaway database of their own. Without it they show as skipped.
/// </summary>
public sealed class PostgresMigrationTests : IAsyncLifetime
{
    private PostgresTestDatabase? _database;

    public async Task InitializeAsync()
    {
        if (PostgresTestDatabase.IsConfigured)
        {
            _database = await PostgresTestDatabase.CreateAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    [PostgresFact]
    public async Task TheMigrations_ApplyToAnEmptyDatabase_AndLeaveNothingPendingInTheModel()
    {
        await using var db = _database!.CreateContext("tenant-a");

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(db.Database.GetMigrations().Count(), (await db.Database.GetAppliedMigrationsAsync()).Count());

        // A model change nobody put in a migration would show here.
        Assert.False(db.Database.HasPendingModelChanges(), "The model differs from the migrations: add a migration.");

        // The catalog has migrations of its own, in the same database, with a history table that does not collide with the tenant's.
        await using var catalog = _database.CreateCatalog("prime");
        await catalog.Database.MigrateAsync();
        Assert.Empty(await catalog.Database.GetPendingMigrationsAsync());
        Assert.False(catalog.Database.HasPendingModelChanges(), "The catalog model differs from its migrations: add a catalog migration.");
        Assert.Equal(
            ["Catalog.__CatalogMigrationsHistory"],
            await db.Database.SqlQueryRaw<string>("""SELECT table_schema || '.' || table_name AS "Value" FROM information_schema.tables WHERE table_name = '__CatalogMigrationsHistory' """).ToListAsync());
    }

    [PostgresFact]
    public async Task TheSeeds_RunOnPostgres_AndTwiceChangeNothing_TheCatalogOnceInThePrimeTenantsDatabase()
    {
        await MigrateAsync();
        await using (var catalog = _database!.CreateCatalog("prime"))
        {
            await catalog.Database.MigrateAsync();
        }

        for (var run = 0; run < 2; run++)
        {
            await using var db = _database!.CreateContext("tenant-a");
            await new SeedDataRunner(db, new ISeedContributor[] { new HrSeedContributor(), new ProfileSeedContributor(Microsoft.Extensions.Options.Options.Create(new SampleDataOptions { TenantIds = ["tenant-a"] })), new AttendanceSeedContributor() }, NullLogger<SeedDataRunner>.Instance)
                .RunAsync(CancellationToken.None);
            await using var catalog = _database.CreateCatalog("prime");
            await new CatalogSeedRunner(catalog, [new CatalogSeedContributor()], NullLogger<CatalogSeedRunner>.Instance).RunAsync(CancellationToken.None);
        }

        await using var check = _database!.CreateContext("tenant-a");
        Assert.Single(check.TimeSchedules);
        Assert.Single(check.AttendancePolicies);
        await using var checkCatalog = _database.CreateCatalog("tenant-a");
        Assert.Equal(4, await checkCatalog.AttendanceVendors.CountAsync());
        Assert.Equal(4, await checkCatalog.AttendanceConnectorDefinitions.CountAsync());
        Assert.Equal(6, await checkCatalog.AttendanceDeviceTypes.CountAsync());
        Assert.Equal(6, await checkCatalog.AttendanceTransportDefinitions.CountAsync());
        Assert.Equal(3, await checkCatalog.AttendancePayloadFormats.CountAsync());
    }

    [PostgresFact]
    public async Task MoveCatalogToPrime_TurnsEveryReferenceIntoAKey_AndDropsTheCatalogTablesOfTheTenant()
    {
        // The database as it was before the catalog moved: a catalog of the tenant's own, and ids that pointed into it.
        await using (var db = _database!.CreateContext("tenant-a"))
        {
            await db.Database.GetService<IMigrator>().MigrateAsync("20261027094334_AddCancelSuppliedPunch");
            const string Stamp = "LOCALTIMESTAMP, 0, LOCALTIMESTAMP, 0, gen_random_uuid()";
            await db.Database.ExecuteSqlRawAsync($"""
                INSERT INTO "Attendance"."AttendanceDeviceType" ("TenantId", "Name", "IsActive", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                VALUES ('tenant-a', 'Facial', true, {Stamp});
                INSERT INTO "Attendance"."AttendanceVendor" ("TenantId", "VendorKey", "DisplayName", "Manufacturer", "WebsiteUrl", "IsActive", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                VALUES ('tenant-a', 'zk', 'ZK', '', '', true, {Stamp});
                INSERT INTO "Attendance"."AttendancePayloadFormat" ("TenantId", "Name", "Description", "Version", "PayloadKind", "Delimiter", "HasHeaderRow", "SheetNumber", "RecordPath", "IsActive", "EmployeeColumn", "TimestampColumn", "DateColumn", "TimeColumn", "TypeColumn", "DirectionColumn", "DeviceColumn", "TransactionIdColumn", "TimestampFormats", "DateFormats", "TimeFormats", "TimestampCulture", "PayloadColumns", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                VALUES ('tenant-a', 'Mine', '', 3, 'Auto', ',', true, 1, '', true, 'id', 'time', '', '', '', '', '', '', 'yyyy-MM-dd HH:mm', '', '', '', '', {Stamp});
                INSERT INTO "Attendance"."AttendanceConnectorDefinition" ("TenantId", "VendorId", "ConnectorKey", "DisplayName", "Description", "AdapterKey", "SupportsIncrementalSync", "EncryptionKind", "IsActive", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                SELECT 'tenant-a', v."Id", 'zk-export', 'ZK export', '', NULL, false, 'None', true, {Stamp} FROM "Attendance"."AttendanceVendor" v;
                INSERT INTO "Attendance"."AttendanceIntegration" ("TenantId", "ConnectorId", "DisplayName", "PayloadKeyRef", "PayloadFormatId", "Status", "HealthStatus", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                SELECT 'tenant-a', c."Id", n.name, '', CASE WHEN n.name = 'Own format' THEN (SELECT "Id" FROM "Attendance"."AttendancePayloadFormat" WHERE "Name" = 'Mine') END, 'Active', 'Unknown', {Stamp}
                FROM "Attendance"."AttendanceConnectorDefinition" c, (VALUES ('Own format'), ('Default format')) AS n(name);
                INSERT INTO "Attendance"."AttendanceDevice" ("TenantId", "IntegrationId", "ExternalDeviceId", "Name", "DeviceTypeId", "Model", "SoftwareVersion", "SdkVersion", "TimeZoneId", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                SELECT 'tenant-a', i."Id", d.id, d.id, CASE WHEN d.id = 'A' THEN (SELECT "Id" FROM "Attendance"."AttendanceDeviceType" WHERE "Name" = 'Facial') END, '', '', '', 'UTC', {Stamp}
                FROM "Attendance"."AttendanceIntegration" i, (VALUES ('A'), ('B')) AS d(id) WHERE i."DisplayName" = 'Own format';
                INSERT INTO "Attendance"."AttendanceProcessingBatch" ("TenantId", "IntegrationId", "BatchType", "StartedAt", "Status", "RecordsIngested", "RecordsFailed", "PayloadFormatId")
                SELECT 'tenant-a', i."Id", b.kind, LOCALTIMESTAMP, 'Succeeded', 0, 0, CASE WHEN b.kind = 'ManualUpload' THEN (SELECT "Id" FROM "Attendance"."AttendancePayloadFormat" WHERE "Name" = 'Mine') END
                FROM "Attendance"."AttendanceIntegration" i, (VALUES ('ManualUpload'), ('IncrementalSync')) AS b(kind) WHERE i."DisplayName" = 'Own format';
                """);
        }

        // Up to this migration only: a later one drops the integration's own format name, which this test reads.
        await MigrateAsync("20261101141127_MoveCatalogToPrime");

        await using var check = _database!.CreateContext("tenant-a");
        async Task<List<string>> Strings(string sql) => await check.Database.SqlQueryRaw<string>(sql).ToListAsync();

        Assert.Equal(
            ["Default format|zk-export|", "Own format|zk-export|Mine"],
            await Strings("""SELECT "DisplayName" || '|' || "ConnectorKey" || '|' || COALESCE("PayloadFormatName", '') AS "Value" FROM "Attendance"."AttendanceIntegration" ORDER BY 1"""));
        Assert.Equal(["A:Facial", "B:"], await Strings("""SELECT "ExternalDeviceId" || ':' || COALESCE("DeviceTypeName", '') AS "Value" FROM "Attendance"."AttendanceDevice" ORDER BY 1"""));
        Assert.Equal(
            ["IncrementalSync||", "ManualUpload|Mine|3"],
            await Strings("""SELECT "BatchType" || '|' || COALESCE("PayloadFormatName", '') || '|' || COALESCE("PayloadFormatVersion"::text, '') AS "Value" FROM "Attendance"."AttendanceProcessingBatch" ORDER BY 1"""));

        // The tenant keeps no copy of the catalog, and nothing in it points at one by foreign key.
        Assert.Equal(
            [],
            await Strings("""SELECT table_name AS "Value" FROM information_schema.tables WHERE table_schema = 'Attendance' AND table_name IN ('AttendanceVendor', 'AttendanceConnectorDefinition', 'AttendanceConnectorTransport', 'AttendanceTransportDefinition', 'AttendanceDeviceType', 'AttendancePayloadFormat', 'AttendancePayloadEventCode') """));
        Assert.False(check.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task InstantsAreStoredInUtc_AndLocalTimesAsTheyAre()
    {
        await MigrateAsync();
        var fixture = await SeedFixtureAsync();

        // A vendor timestamp with an offset: Npgsql refuses a non-zero offset, so the context converts it.
        var local = new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.FromHours(3));
        await using (var db = _database!.CreateContext("tenant-a"))
        {
            db.RawAttendanceTransactions.Add(NewRaw(fixture, local));
            await db.SaveChangesAsync();
        }

        await using var check = _database!.CreateContext("tenant-a");

        // The fixture already holds one raw row (no parsed instant); this test's is the one that has one.
        var raw = await check.RawAttendanceTransactions.SingleAsync(r => r.TransactionTimestampParsed != null);
        Assert.Equal(local, raw.TransactionTimestampParsed);
        Assert.Equal(TimeSpan.Zero, raw.TransactionTimestampParsed!.Value.Offset);
        Assert.Equal(new DateTime(2026, 10, 5, 6, 0, 0), raw.TransactionTimestampParsed.Value.DateTime);
        Assert.Equal("tenant-a", TenantIdOf(check, raw));
    }

    [PostgresFact]
    public async Task TheDuplicateKey_IsAFilteredUniqueIndex_OnPostgres()
    {
        await MigrateAsync();
        var fixture = await SeedFixtureAsync();
        var bucket = new DateTimeOffset(2026, 10, 5, 6, 0, 0, TimeSpan.Zero);

        await using var db = _database!.CreateContext("tenant-a");
        var recorder = new NormalizedTransactionRecorder(db);
        var first = await recorder.RecordAsync(NewCheckIn(fixture, bucket));
        var second = await recorder.RecordAsync(NewCheckIn(fixture, bucket));

        // The second is kept, as a duplicate, instead of tripping the unique index.
        Assert.Equal(DuplicateStatus.Unique, first.DuplicateStatus);
        Assert.Equal(DuplicateStatus.DuplicateConfirmed, second.DuplicateStatus);
        Assert.Equal(first.Id, second.DuplicateOfTransactionId);
        Assert.Equal(2, await db.NormalizedAttendanceTransactions.CountAsync());

        // Two Unique rows with the same key are refused by the database itself.
        db.NormalizedAttendanceTransactions.Add(NewCheckIn(fixture, bucket));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)error.InnerException!).SqlState);
    }

    [PostgresFact]
    public async Task RecordsDaysIntervalsAndTheAuditLog_RoundTrip()
    {
        await MigrateAsync();
        var fixture = await SeedFixtureAsync();
        var monday = new DateTime(2026, 10, 5);

        await using (var db = _database!.CreateContext("tenant-a"))
        {
            db.AttendanceRecords.Add(new AttendanceRecord
            {
                EmployeeId = fixture.EmployeeId,
                AttendanceDate = monday,
                ScheduledStart = monday.AddHours(8),
                ActualCheckIn = monday.AddHours(8).AddMinutes(12),
                WorkingHours = new TimeSpan(9, 23, 0),
                OvertimeMinutes = 83,
                Status = AttendanceRecordStatus.PendingReview,
            });
            db.AttendanceDaySummaries.Add(new AttendanceDaySummary { EmployeeId = fixture.EmployeeId, AttendanceDate = monday, TotalWorkingHours = new TimeSpan(9, 23, 0), ShiftCount = 1 });
            db.AttendanceAuditLogs.Add(new AttendanceAuditLog { Actor = "system", Action = "Test", TargetType = "Thing", TargetId = 1, Timestamp = DateTimeOffset.UtcNow });
            db.PayPeriods.Add(new PayPeriod { StartDate = monday, EndDate = monday.AddDays(6) });
            await db.SaveChangesAsync();
        }

        await using var check = _database!.CreateContext("tenant-a");
        var record = await check.AttendanceRecords.SingleAsync();
        Assert.Equal((new TimeSpan(9, 23, 0), monday.AddHours(8).AddMinutes(12), AttendanceRecordStatus.PendingReview), (record.WorkingHours, record.ActualCheckIn, record.Status));
        Assert.Single(check.AttendanceDaySummaries);
        Assert.Single(check.AttendanceAuditLogs);

        // The one-row-per-period index holds on PostgreSQL too.
        check.PayPeriods.Add(new PayPeriod { StartDate = monday, EndDate = monday.AddDays(6) });
        await Assert.ThrowsAsync<DbUpdateException>(() => check.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task ATenantsRows_AreInvisibleToAnother_AndItsWritesAreGuarded()
    {
        await MigrateAsync();
        await SeedFixtureAsync();

        await using var other = _database!.CreateContext("tenant-b");
        Assert.Empty(other.Profiles);
        Assert.Empty(other.AttendanceIntegrations);

        await using var owner = _database.CreateContext("tenant-a");
        Assert.Single(owner.Profiles);
    }

    [PostgresFact]
    public async Task AddConnectorDefinitions_GivesEveryVendorAConnector_AndMovesItsTransportsFormatAndIntegrations()
    {
        // The database as it was before connectors: vendors carried the transports, the default format and the support flags.
        await using (var db = _database!.CreateContext("tenant-a"))
        {
            await db.Database.GetService<IMigrator>().MigrateAsync("20261024200846_RenameVendorAndPayloadFormat");
            const string Stamp = "LOCALTIMESTAMP, 0, LOCALTIMESTAMP, 0, gen_random_uuid()";
            await db.Database.ExecuteSqlRawAsync($"""
                INSERT INTO "Attendance"."AttendanceTransportDefinition" ("TenantId", "TransportKey", "DisplayName", "IsPull", "UsesPayloadFormat", "IsActive", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                VALUES ('tenant-a', 'RestApi', 'REST API', true, false, true, {Stamp}), ('tenant-a', 'Webhook', 'Webhook', false, false, true, {Stamp});
                INSERT INTO "Attendance"."AttendancePayloadFormat" ("TenantId", "Name", "Description", "PayloadKind", "Delimiter", "HasHeaderRow", "SheetNumber", "IsActive", "EmployeeColumn", "TimestampColumn", "DateColumn", "TimeColumn", "TypeColumn", "DirectionColumn", "DeviceColumn", "TransactionIdColumn", "TimestampFormats", "DateFormats", "TimeFormats", "TimestampCulture", "PayloadColumns", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                VALUES ('tenant-a', 'Mine', '', 'Auto', ',', true, 1, true, 'id', 'time', '', '', '', '', '', '', 'yyyy-MM-dd HH:mm', '', '', '', '', {Stamp});
                INSERT INTO "Attendance"."AttendanceVendor" ("TenantId", "VendorKey", "DisplayName", "Manufacturer", "WebsiteUrl", "DefaultPayloadFormatId", "SupportedTransports", "SupportsWebhooks", "SupportsIncrementalSync", "IsActive", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                VALUES
                    ('tenant-a', 'zk', 'ZK', '', '', (SELECT "Id" FROM "Attendance"."AttendancePayloadFormat" WHERE "Name" = 'Mine'), '', false, false, true, {Stamp}),
                    ('tenant-a', 'hook', 'Hook', '', '', NULL, '', true, false, true, {Stamp}),
                    ('tenant-a', 'api', 'Api', '', '', NULL, '', false, true, false, {Stamp});
                INSERT INTO "Attendance"."AttendanceVendorTransport" ("TenantId", "VendorId", "TransportId")
                SELECT 'tenant-a', v."Id", t."Id" FROM "Attendance"."AttendanceVendor" v, "Attendance"."AttendanceTransportDefinition" t WHERE v."VendorKey" = 'api' AND t."TransportKey" = 'RestApi';
                INSERT INTO "Attendance"."AttendanceIntegration" ("TenantId", "VendorId", "DisplayName", "ConnectionConfigRef", "Transport", "PayloadFormatId", "DefaultDeviceId", "TimeZoneId", "PollingScheduleCron", "SyncCursor", "LastSuccessfulSyncAt", "Status", "HealthStatus", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                SELECT 'tenant-a', v."Id", 'Integration of ' || v."VendorKey", '', 'FileUpload', NULL, NULL, NULL, NULL, NULL, NULL, 'Active', 'Unknown', {Stamp}
                FROM "Attendance"."AttendanceVendor" v;
                """);
        }

        await MigrateAsync("20261024201959_AddConnectorDefinitions");

        await using var check = _database!.CreateContext("tenant-a");
        async Task<List<string>> Strings(string sql) => await check.Database.SqlQueryRaw<string>(sql).ToListAsync();

        // One connector per vendor, with the vendor's key; one read by its format alone has no adapter key.
        Assert.Equal(
            ["api|api", "hook|hook", "zk|"],
            await Strings("""SELECT c."ConnectorKey" || '|' || COALESCE(c."AdapterKey", '') AS "Value" FROM "Attendance"."AttendanceConnectorDefinition" c ORDER BY c."ConnectorKey" """));
        Assert.Equal(
            ["api:RestApi", "hook:Webhook"],
            await Strings("""SELECT c."ConnectorKey" || ':' || td."TransportKey" AS "Value" FROM "Attendance"."AttendanceConnectorTransport" ct JOIN "Attendance"."AttendanceConnectorDefinition" c ON c."Id" = ct."ConnectorId" JOIN "Attendance"."AttendanceTransportDefinition" td ON td."Id" = ct."TransportId" ORDER BY c."ConnectorKey" """));
        Assert.Equal(
            ["api:true:false", "hook:false:true", "zk:false:true"],
            await Strings("""SELECT c."ConnectorKey" || ':' || c."SupportsIncrementalSync"::text || ':' || c."IsActive"::text AS "Value" FROM "Attendance"."AttendanceConnectorDefinition" c ORDER BY c."ConnectorKey" """));

        // Each integration points at its vendor's connector, and the default format moved with it.
        Assert.Equal(
            ["Integration of api:api", "Integration of hook:hook", "Integration of zk:zk"],
            await Strings("""SELECT i."DisplayName" || ':' || c."ConnectorKey" AS "Value" FROM "Attendance"."AttendanceIntegration" i JOIN "Attendance"."AttendanceConnectorDefinition" c ON c."Id" = i."ConnectorId" ORDER BY i."DisplayName" """));
        Assert.Equal(["Mine:zk"], await Strings("""SELECT f."Name" || ':' || c."ConnectorKey" AS "Value" FROM "Attendance"."AttendanceConnectorDefinition" c JOIN "Attendance"."AttendancePayloadFormat" f ON f."Id" = c."DefaultPayloadFormatId" """));
        Assert.Equal(["zk"], await Strings("""SELECT c."ConnectorKey" AS "Value" FROM "Attendance"."AttendancePayloadFormat" f JOIN "Attendance"."AttendanceConnectorDefinition" c ON c."Id" = f."ConnectorId" """));

        // What the connector replaced is gone.
        Assert.Equal([], await Strings("""SELECT table_name AS "Value" FROM information_schema.tables WHERE table_schema = 'Attendance' AND table_name = 'AttendanceVendorTransport' """));
    }

    [PostgresFact]
    public async Task AddConnections_GivesEveryIntegrationADefaultConnection_CarryingItsTransportSecretAndSchedule()
    {
        // The database as it was before connections: the integration carried its transport, secret reference and schedule.
        await using (var db = _database!.CreateContext("tenant-a"))
        {
            await db.Database.GetService<IMigrator>().MigrateAsync("20261025044350_AddStructuredPayloadsAndDecryption");
            const string Stamp = "LOCALTIMESTAMP, 0, LOCALTIMESTAMP, 0, gen_random_uuid()";
            await db.Database.ExecuteSqlRawAsync($"""
                INSERT INTO "Attendance"."AttendanceVendor" ("TenantId", "VendorKey", "DisplayName", "Manufacturer", "WebsiteUrl", "SupportedTransports", "IsActive", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                VALUES ('tenant-a', 'suprema-biostar', 'BioStar', '', '', '', true, {Stamp}), ('tenant-a', 'hook', 'Hook', '', '', '', true, {Stamp}), ('tenant-a', 'csv', 'Csv', '', '', '', true, {Stamp});
                INSERT INTO "Attendance"."AttendanceConnectorDefinition" ("TenantId", "VendorId", "ConnectorKey", "DisplayName", "Description", "AdapterKey", "SupportsIncrementalSync", "EncryptionKind", "IsActive", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                SELECT 'tenant-a', v."Id", v."VendorKey", v."DisplayName", '', CASE WHEN v."VendorKey" = 'csv' THEN NULL ELSE v."VendorKey" END, false, 'None', true, {Stamp}
                FROM "Attendance"."AttendanceVendor" v;
                INSERT INTO "Attendance"."AttendanceIntegration" ("TenantId", "ConnectorId", "DisplayName", "ConnectionConfigRef", "PayloadKeyRef", "Transport", "PayloadFormatId", "DefaultDeviceId", "TimeZoneId", "PollingScheduleCron", "SyncCursor", "LastSuccessfulSyncAt", "Status", "HealthStatus", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                SELECT 'tenant-a', c."Id", 'Integration of ' || c."ConnectorKey", 'ref-' || c."ConnectorKey",
                       '', CASE c."ConnectorKey" WHEN 'suprema-biostar' THEN 'RestApi' WHEN 'hook' THEN 'Webhook' ELSE 'FileUpload' END,
                       NULL, NULL, NULL, CASE c."ConnectorKey" WHEN 'suprema-biostar' THEN '*/5 * * * *' ELSE NULL END,
                       NULL, NULL, 'Active', 'Unknown', {Stamp}
                FROM "Attendance"."AttendanceConnectorDefinition" c;
                """);
        }

        await MigrateAsync("20261025102737_AddConnections");

        await using var check = _database!.CreateContext("tenant-a");
        async Task<List<string>> Strings(string sql) => await check.Database.SqlQueryRaw<string>(sql).ToListAsync();

        // One default connection per integration, with what the integration had; the authentication is derived.
        string[] expected =
        [
            "Integration of csv|Default|true|FileUpload|None|ref-csv|",
            "Integration of hook|Default|true|Webhook|HmacSignature|ref-hook|",
            "Integration of suprema-biostar|Default|true|RestApi|SessionLogin|ref-suprema-biostar|*/5 * * * *",
        ];
        const string Copied = """
            SELECT i."DisplayName" || '|' || c."Name" || '|' || c."IsDefault"::text || '|' || c."Transport" || '|' || c."AuthMethod" || '|' || c."SecretRef" || '|' || COALESCE(c."PollingSchedule", '') AS "Value"
            FROM "Attendance"."AttendanceConnection" c JOIN "Attendance"."AttendanceIntegration" i ON i."Id" = c."IntegrationId" ORDER BY i."DisplayName"
            """;
        Assert.Equal(expected, await Strings(Copied));

        // What the connection replaced is gone from the integration, and a second default is refused by the database.
        Assert.Equal([], await Strings("""SELECT column_name AS "Value" FROM information_schema.columns WHERE table_schema = 'Attendance' AND table_name = 'AttendanceIntegration' AND column_name IN ('Transport', 'ConnectionConfigRef', 'PollingScheduleCron') """));
        const string SecondDefault = """
            INSERT INTO "Attendance"."AttendanceConnection" ("TenantId", "IntegrationId", "Name", "IsDefault", "Transport", "AuthMethod", "Settings", "SecretRef", "IsActive", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
            SELECT 'tenant-a', "Id", 'Second', TRUE, 'FileUpload', 'None', '{}', '', TRUE, LOCALTIMESTAMP, 0, LOCALTIMESTAMP, 0, gen_random_uuid() FROM "Attendance"."AttendanceIntegration" LIMIT 1
            """;
        await Assert.ThrowsAnyAsync<Exception>(() => check.Database.ExecuteSqlRawAsync(SecondDefault));
    }

    [PostgresFact]
    public async Task AddPayloadFormatVersions_StartsEveryFormatAtVersionOne_AndTellsPastUploadBatchesWhichFormatReadThem()
    {
        await using (var db = _database!.CreateContext("tenant-a"))
        {
            await db.Database.GetService<IMigrator>().MigrateAsync("20261025102737_AddConnections");
            const string Stamp = "LOCALTIMESTAMP, 0, LOCALTIMESTAMP, 0, gen_random_uuid()";
            await db.Database.ExecuteSqlRawAsync($"""
                INSERT INTO "Attendance"."AttendancePayloadFormat" ("TenantId", "Name", "Description", "PayloadKind", "Delimiter", "HasHeaderRow", "SheetNumber", "IsActive", "EmployeeColumn", "TimestampColumn", "DateColumn", "TimeColumn", "TypeColumn", "DirectionColumn", "DeviceColumn", "TransactionIdColumn", "TimestampFormats", "DateFormats", "TimeFormats", "TimestampCulture", "PayloadColumns", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                VALUES ('tenant-a', 'Default format', '', 'Auto', ',', true, 1, true, 'id', 'time', '', '', '', '', '', '', 'yyyy-MM-dd HH:mm', '', '', '', '', {Stamp}),
                       ('tenant-a', 'Own format', '', 'Auto', ',', true, 1, true, 'id', 'time', '', '', '', '', '', '', 'dd/MM/yyyy HH:mm', '', '', '', '', {Stamp});
                INSERT INTO "Attendance"."AttendanceVendor" ("TenantId", "VendorKey", "DisplayName", "Manufacturer", "WebsiteUrl", "SupportedTransports", "IsActive", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                VALUES ('tenant-a', 'zk', 'ZK', '', '', '', true, {Stamp});
                INSERT INTO "Attendance"."AttendanceConnectorDefinition" ("TenantId", "VendorId", "ConnectorKey", "DisplayName", "Description", "AdapterKey", "DefaultPayloadFormatId", "SupportsIncrementalSync", "EncryptionKind", "IsActive", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                SELECT 'tenant-a', v."Id", 'zk', 'ZK', '', NULL, (SELECT "Id" FROM "Attendance"."AttendancePayloadFormat" WHERE "Name" = 'Default format'), false, 'None', true, {Stamp}
                FROM "Attendance"."AttendanceVendor" v;
                INSERT INTO "Attendance"."AttendanceIntegration" ("TenantId", "ConnectorId", "DisplayName", "PayloadKeyRef", "PayloadFormatId", "Status", "HealthStatus", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                SELECT 'tenant-a', c."Id", n.name, '', (SELECT "Id" FROM "Attendance"."AttendancePayloadFormat" WHERE "Name" = 'Own format' AND n.name = 'Uses its own'), 'Active', 'Unknown', {Stamp}
                FROM "Attendance"."AttendanceConnectorDefinition" c, (VALUES ('Uses its own'), ('Uses the default')) AS n(name);
                INSERT INTO "Attendance"."AttendanceProcessingBatch" ("TenantId", "IntegrationId", "BatchType", "StartedAt", "Status", "RecordsIngested", "RecordsFailed")
                SELECT 'tenant-a', i."Id", b.kind, LOCALTIMESTAMP, 'Succeeded', 0, 0
                FROM "Attendance"."AttendanceIntegration" i, (VALUES ('ManualUpload'), ('IncrementalSync')) AS b(kind);
                """);
        }

        await MigrateAsync("20261025105435_AddPayloadFormatVersions");

        await using var check = _database!.CreateContext("tenant-a");
        async Task<List<string>> Strings(string sql) => await check.Database.SqlQueryRaw<string>(sql).ToListAsync();
        Assert.Equal(["1", "1"], await Strings("""SELECT "Version"::text AS "Value" FROM "Attendance"."AttendancePayloadFormat" """));

        // An upload batch is told the format its integration used (its own, else the connector's default); a sync batch reads none.
        string[] expected = ["Uses its own|IncrementalSync|", "Uses its own|ManualUpload|Own format", "Uses the default|IncrementalSync|", "Uses the default|ManualUpload|Default format"];
        const string Backfilled = """
            SELECT i."DisplayName" || '|' || b."BatchType" || '|' || COALESCE(f."Name", '') AS "Value"
            FROM "Attendance"."AttendanceProcessingBatch" b JOIN "Attendance"."AttendanceIntegration" i ON i."Id" = b."IntegrationId"
            LEFT JOIN "Attendance"."AttendancePayloadFormat" f ON f."Id" = b."PayloadFormatId" ORDER BY 1
            """;
        Assert.Equal(expected, await Strings(Backfilled));
    }

    [PostgresFact]
    public async Task DropLegacyCatalogColumns_MovesWhatTheSeederNeverMoved_ThenDropsTheTextColumns()
    {
        await using (var db = _database!.CreateContext("tenant-a"))
        {
            await db.Database.GetService<IMigrator>().MigrateAsync("20261025105435_AddPayloadFormatVersions");
            const string Stamp = "LOCALTIMESTAMP, 0, LOCALTIMESTAMP, 0, gen_random_uuid()";
            await db.Database.ExecuteSqlRawAsync($"""
                INSERT INTO "Attendance"."AttendanceTransportDefinition" ("TenantId", "TransportKey", "DisplayName", "IsPull", "UsesPayloadFormat", "IsActive", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                VALUES ('tenant-a', 'FileUpload', 'Upload', false, true, true, {Stamp}), ('tenant-a', 'Sftp', 'SFTP', true, false, true, {Stamp});
                INSERT INTO "Attendance"."AttendanceDeviceType" ("TenantId", "Name", "IsActive", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                VALUES ('tenant-a', 'Facial', true, {Stamp});
                INSERT INTO "Attendance"."AttendanceVendor" ("TenantId", "VendorKey", "DisplayName", "Manufacturer", "WebsiteUrl", "SupportedTransports", "IsActive", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                VALUES ('tenant-a', 'old', 'Old', '', '', 'FileUpload, sftp,Carrier pigeon', true, {Stamp}), ('tenant-a', 'moved', 'Moved', '', '', '', true, {Stamp});
                INSERT INTO "Attendance"."AttendanceConnectorDefinition" ("TenantId", "VendorId", "ConnectorKey", "DisplayName", "Description", "AdapterKey", "SupportsIncrementalSync", "EncryptionKind", "IsActive", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                SELECT 'tenant-a', v."Id", c.key, c.key, '', NULL, false, 'None', true, {Stamp}
                FROM "Attendance"."AttendanceVendor" v JOIN (VALUES ('old', 'old'), ('old', 'old-second'), ('moved', 'moved')) AS c(vendor, key) ON c.vendor = v."VendorKey";
                INSERT INTO "Attendance"."AttendanceIntegration" ("TenantId", "ConnectorId", "DisplayName", "PayloadKeyRef", "Status", "HealthStatus", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                SELECT 'tenant-a', c."Id", 'Clock', '', 'Active', 'Unknown', {Stamp} FROM "Attendance"."AttendanceConnectorDefinition" c WHERE c."ConnectorKey" = 'old';
                INSERT INTO "Attendance"."AttendanceDevice" ("TenantId", "IntegrationId", "ExternalDeviceId", "Name", "DeviceType", "Model", "SoftwareVersion", "SdkVersion", "TimeZoneId", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                SELECT 'tenant-a', i."Id", d.id, d.id, d.type, '', '', '', 'UTC', {Stamp}
                FROM "Attendance"."AttendanceIntegration" i, (VALUES ('A', 'facial'), ('B', 'Palm reader'), ('C', 'palm READER'), ('D', '')) AS d(id, type);
                """);
        }

        await MigrateAsync("20261025110110_DropLegacyCatalogColumns");

        await using var check = _database!.CreateContext("tenant-a");
        async Task<List<string>> Strings(string sql) => await check.Database.SqlQueryRaw<string>(sql).ToListAsync();

        // The main connector (the one with the vendor's key) took the transport the text named; FileUpload and the unknown name were skipped.
        Assert.Equal(
            ["old:Sftp"],
            await Strings("""SELECT c."ConnectorKey" || ':' || td."TransportKey" AS "Value" FROM "Attendance"."AttendanceConnectorTransport" ct JOIN "Attendance"."AttendanceConnectorDefinition" c ON c."Id" = ct."ConnectorId" JOIN "Attendance"."AttendanceTransportDefinition" td ON td."Id" = ct."TransportId" ORDER BY 1"""));

        // The device types: the listed one matched without regard to case, an unlisted one created once, a blank one left with none.
        Assert.Equal(
            ["A:Facial", "B:Palm reader", "C:Palm reader", "D:"],
            await Strings("""SELECT d."ExternalDeviceId" || ':' || COALESCE(t."Name", '') AS "Value" FROM "Attendance"."AttendanceDevice" d LEFT JOIN "Attendance"."AttendanceDeviceType" t ON t."Id" = d."DeviceTypeId" ORDER BY 1"""));
        Assert.Equal(["Facial", "Palm reader"], await Strings("""SELECT "Name" AS "Value" FROM "Attendance"."AttendanceDeviceType" ORDER BY 1"""));
        Assert.Equal(
            [],
            await Strings("""SELECT table_name || '.' || column_name AS "Value" FROM information_schema.columns WHERE table_schema = 'Attendance' AND ((table_name = 'AttendanceVendor' AND column_name = 'SupportedTransports') OR (table_name = 'AttendanceDevice' AND column_name = 'DeviceType'))"""));
    }

    [PostgresFact]
    public async Task UnifyApprovals_TurnsEveryRequestAndEveryReviewIntoOneApproval_AndQueuesTheRecordsWaitingForReview()
    {
        // The database as it was before the approvals were one: corrections carried their own status and decisions (a request is the rows
        // that share a RequestId), review decisions were rows of their own, and a record waiting for review was only a status.
        int employee, manager, hr, approvedRecord, waitingRecord, rejectedRecord, plainWaitingRecord;
        var (requestA, requestB, requestC) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using (var db = _database!.CreateContext("tenant-a"))
        {
            await db.Database.GetService<IMigrator>().MigrateAsync("20261104154314_DropIntegrationPayloadFormatName");
            var people = new[]
            {
                new Profile { EmployeeNumber = "E-1", FirstName = "Ada", LastName = "Lovelace", Title = "Ada Lovelace" },
                new Profile { EmployeeNumber = "M-1", FirstName = "Grace", LastName = "Hopper", Title = "Grace Hopper" },
                new Profile { EmployeeNumber = "H-1", FirstName = "Alan", LastName = "Turing", Title = "Alan Turing" },
            };
            db.AddRange(people);
            await db.SaveChangesAsync();
            (employee, manager, hr) = (people[0].Id, people[1].Id, people[2].Id);

            var monday = new DateTime(2026, 10, 5);
            AttendanceRecord Record(int offset, int version, bool current, AttendanceRecordStatus status, int overtime) =>
                new() { EmployeeId = employee, AttendanceDate = monday.AddDays(offset), Version = version, IsCurrent = current, Status = status, OvertimeMinutes = overtime };
            var records = new[]
            {
                Record(0, 1, false, AttendanceRecordStatus.Approved, 90),
                Record(0, 2, true, AttendanceRecordStatus.PendingReview, 30),
                Record(1, 1, true, AttendanceRecordStatus.Rejected, 0),
                Record(2, 1, true, AttendanceRecordStatus.PendingReview, 0),
            };
            db.AddRange(records);
            await db.SaveChangesAsync();
            (approvedRecord, waitingRecord, rejectedRecord, plainWaitingRecord) = (records[0].Id, records[1].Id, records[2].Id, records[3].Id);

            const string Stamp = "LOCALTIMESTAMP, 0, LOCALTIMESTAMP, 0, gen_random_uuid()";
            var oldRows = $"""
                INSERT INTO "Attendance"."AttendanceReviewDecision" ("TenantId", "AttendanceRecordId", "EmployeeId", "AttendanceDate", "Outcome", "DecidedById", "DecidedByRole", "DecidedAt", "Reason", "OvertimeMinutes", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                VALUES ('tenant-a', {approvedRecord}, {employee}, '2026-10-05', 'Approved', {manager}, 'Manager', '2026-10-06 09:00', NULL, 90, {Stamp}),
                       ('tenant-a', {rejectedRecord}, {employee}, '2026-10-06', 'Rejected', {hr}, 'HR', '2026-10-07 09:00', 'No', 0, {Stamp});

                INSERT INTO "Attendance"."AttendanceCorrection" ("TenantId", "AttendanceExceptionId", "RequestId", "EmployeeId", "AttendanceDate", "Kind", "OriginalEventType", "CorrectedEventType", "ProposedValue", "TimeZoneId", "Justification", "RequestedByEmployeeId", "RequestedAt", "ApprovalWorkflow", "Status", "ManagerDecision", "ManagerDecidedById", "ManagerDecidedAt", "HrDecision", "HrDecidedById", "HrDecidedAt", "AppliedAt", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                VALUES ('tenant-a', NULL, '{requestA}', {employee}, '2026-10-05', 'SupplyPunch', NULL, 'CheckIn', '2026-10-05 08:00', 'UTC', 'Request A', {employee}, '2026-10-06 08:00', 'Both', 'Applied', 'Approved', {manager}, '2026-10-06 09:00', 'Approved', {hr}, '2026-10-06 10:00', '2026-10-06 10:00', {Stamp}),
                       ('tenant-a', NULL, '{requestA}', {employee}, '2026-10-05', 'IgnorePunch', 'CheckOut', 'CheckOut', '2026-10-05 17:00', 'UTC', 'Request A', {employee}, '2026-10-06 08:00', 'Both', 'Applied', 'Approved', {manager}, '2026-10-06 09:00', 'Approved', {hr}, '2026-10-06 10:00', '2026-10-06 10:00', {Stamp}),
                       ('tenant-a', NULL, '{requestB}', {employee}, '2026-10-06', 'SupplyPunch', NULL, 'CheckIn', '2026-10-06 08:00', 'UTC', 'Request B', {employee}, '2026-10-07 08:00', 'Both', 'Pending', 'Approved', {manager}, '2026-10-07 09:00', NULL, NULL, NULL, NULL, {Stamp}),
                       ('tenant-a', NULL, '{requestC}', {employee}, '2026-10-07', 'SupplyPunch', NULL, 'CheckOut', '2026-10-07 17:00', 'UTC', 'Request C', {employee}, '2026-10-08 08:00', 'Manager', 'Rejected', 'Rejected', {manager}, '2026-10-08 09:00', NULL, NULL, NULL, NULL, {Stamp});
                """;
            await db.Database.ExecuteSqlRawAsync(oldRows);
        }

        await MigrateAsync();

        await using var check = _database!.CreateContext("tenant-a");
        async Task<List<string>> Strings(string sql) => (await check.Database.SqlQueryRaw<string>(sql).ToListAsync()).OrderBy(s => s, StringComparer.Ordinal).ToList();

        // One approval per request and one per review decision, and one pending review for each current record waiting for review.
        Assert.Equal(
            new[]
            {
                "Correction|Applied|Both|",
                "Correction|Pending|Both|",
                "Correction|Rejected|Manager|",
                $"Review|Approved|ManagerOrHr|{approvedRecord}",
                $"Review|Pending|ManagerOrHr|{plainWaitingRecord}",
                $"Review|Pending|ManagerOrHr|{waitingRecord}",
                $"Review|Rejected|ManagerOrHr|{rejectedRecord}",
            }.OrderBy(s => s, StringComparer.Ordinal),
            await Strings("""SELECT "Type" || '|' || "Status" || '|' || "Workflow" || '|' || COALESCE("AttendanceRecordId"::text, '') AS "Value" FROM "Attendance"."AttendanceApproval" """));

        // The edits of one request share its approval; only a request still waiting holds its exception.
        Assert.Equal(
            ["Request A|Applied|false", "Request A|Applied|false", "Request B|Pending|true", "Request C|Rejected|false"],
            await Strings("""SELECT c."Justification" || '|' || a."Status" || '|' || c."IsPending"::text AS "Value" FROM "Attendance"."AttendanceCorrection" c JOIN "Attendance"."AttendanceApproval" a ON a."Id" = c."AttendanceApprovalId" """));
        Assert.Equal(["1"], await Strings("""SELECT COUNT(DISTINCT "AttendanceApprovalId")::text AS "Value" FROM "Attendance"."AttendanceCorrection" WHERE "Justification" = 'Request A' """));

        // The decisions of a request are kept as they were, step by step.
        Assert.Equal(
            [$"Approved/{manager}|Approved/{hr}|true"],
            await Strings("""SELECT a."ManagerDecision" || '/' || a."ManagerDecidedById"::text || '|' || a."HrDecision" || '/' || a."HrDecidedById"::text || '|' || (a."AppliedAt" IS NOT NULL)::text AS "Value" FROM "Attendance"."AttendanceApproval" a JOIN "Attendance"."AttendanceCorrection" c ON c."AttendanceApprovalId" = a."Id" WHERE c."Justification" = 'Request A' LIMIT 1"""));

        // A review decided by a manager fills the manager's slot; by HR, HR's. The reason and the overtime it covered come along.
        var decidedReviews = $"""
            SELECT COALESCE("ManagerDecidedById"::text, '-') || '/' || COALESCE("HrDecidedById"::text, '-') || '|' || "OvertimeMinutes"::text || '|' || COALESCE("Reason", '') AS "Value"
            FROM "Attendance"."AttendanceApproval" WHERE "AttendanceRecordId" IN ({approvedRecord}, {rejectedRecord}) ORDER BY "AttendanceRecordId"
            """;
        Assert.Equal([$"{manager}/-|90|", $"-/{hr}|0|No"], await check.Database.SqlQueryRaw<string>(decidedReviews).ToListAsync());

        // What the approvals replaced is gone.
        Assert.Equal([], await Strings("""SELECT table_name AS "Value" FROM information_schema.tables WHERE table_schema = 'Attendance' AND table_name = 'AttendanceReviewDecision' """));
        Assert.Equal([], await Strings("""SELECT column_name AS "Value" FROM information_schema.columns WHERE table_schema = 'Attendance' AND table_name = 'AttendanceCorrection' AND column_name IN ('RequestId', 'Status', 'ApprovalWorkflow', 'ManagerDecision', 'ManagerDecidedById', 'HrDecision', 'HrDecidedById', 'AppliedAt') """));
        Assert.Equal([], await Strings("""SELECT column_name AS "Value" FROM information_schema.columns WHERE table_schema = 'Attendance' AND table_name = 'AttendanceApproval' AND column_name = 'MigrationKey' """));

        // A record has one pending review at a time: the database refuses a second.
        var second = $"""
            INSERT INTO "Attendance"."AttendanceApproval" ("TenantId", "Type", "EmployeeId", "AttendanceDate", "Status", "Workflow", "RequestedAt", "AttendanceRecordId", "OvertimeMinutes", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
            VALUES ('tenant-a', 'Review', {employee}, '2026-10-05', 'Pending', 'ManagerOrHr', LOCALTIMESTAMP, {waitingRecord}, 0, LOCALTIMESTAMP, 0, LOCALTIMESTAMP, 0, gen_random_uuid())
            """;
        await Assert.ThrowsAnyAsync<Exception>(() => check.Database.ExecuteSqlRawAsync(second));

        // The model and the migrations agree.
        Assert.False(check.Database.HasPendingModelChanges(), "The model differs from the migrations: add a migration.");
    }

    private static string? TenantIdOf(RobeemDbContext db, object entity) =>
        db.Entry(entity).Property("TenantId").CurrentValue as string;

    private static RawAttendanceTransaction NewRaw(Fixture fixture, DateTimeOffset parsed) => new()
    {
        IntegrationId = fixture.IntegrationId,
        ImportBatchId = fixture.BatchId,
        TransactionTimestampRaw = "2026-10-05 09:00:00+03:00",
        TransactionTimestampParsed = parsed,
        RawPayload = "{}",
        SourceSystem = "generic-csv",
        RecordHash = Guid.NewGuid().ToString("N"),
    };

    private static NormalizedAttendanceTransaction NewCheckIn(Fixture fixture, DateTimeOffset bucket) => new()
    {
        EmployeeId = fixture.EmployeeId,
        DeviceId = fixture.DeviceId,
        IntegrationId = fixture.IntegrationId,
        RawTransactionId = fixture.RawId,
        AttendanceDate = DateTime.SpecifyKind(bucket.UtcDateTime.Date, DateTimeKind.Unspecified),
        EventTimestampUtc = bucket.AddSeconds(5),
        EventTimestampLocal = DateTime.SpecifyKind(bucket.UtcDateTime.AddSeconds(5), DateTimeKind.Unspecified),
        TimeZoneId = "UTC",
        NormalizedEventType = NormalizedEventType.CheckIn,
        DuplicateBucketTimestampUtc = bucket,
    };

    /// <summary>Applies the migrations, all of them or up to <paramref name="target"/>.</summary>
    private async Task MigrateAsync(string? target = null)
    {
        await using var db = _database!.CreateContext("tenant-a");
        if (target is null)
        {
            await db.Database.MigrateAsync();
        }
        else
        {
            await db.Database.GetService<IMigrator>().MigrateAsync(target);
        }
    }

    private async Task<Fixture> SeedFixtureAsync()
    {
        await using var db = _database!.CreateContext("tenant-a");
        var employee = new Profile { EmployeeNumber = "E-1", FirstName = "Ada", LastName = "Lovelace", Title = "Ada Lovelace" };
        var integration = new AttendanceIntegration
        {
            ConnectorKey = "generic-csv",
            DisplayName = "HQ",
        };
        var device = new AttendanceDevice { Integration = integration, ExternalDeviceId = "DEV-1", Name = "Front door", TimeZoneId = "UTC" };
        var batch = new AttendanceProcessingBatch { Integration = integration, BatchType = AttendanceBatchType.ManualUpload };
        var raw = new RawAttendanceTransaction
        {
            Integration = integration,
            ImportBatch = batch,
            TransactionTimestampRaw = "-",
            RawPayload = "{}",
            SourceSystem = "generic-csv",
            RecordHash = "seed",
        };
        db.AddRange(employee, device, raw);
        await db.SaveChangesAsync();
        return new Fixture(employee.Id, integration.Id, device.Id, batch.Id, raw.Id);
    }

    private sealed record Fixture(int EmployeeId, int IntegrationId, int DeviceId, int BatchId, int RawId);

    /// <summary>A throwaway database on the configured server, dropped at the end.</summary>
    private sealed class PostgresTestDatabase : IAsyncDisposable
    {
        public const string Variable = "ROBEEM_TEST_POSTGRES";

        private readonly NpgsqlConnectionStringBuilder _server;
        private readonly string _name;

        private PostgresTestDatabase(NpgsqlConnectionStringBuilder server, string name)
        {
            _server = server;
            _name = name;
        }

        public static bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable));

        public static async Task<PostgresTestDatabase> CreateAsync()
        {
            var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(Variable)) { Database = "postgres", Pooling = false };
            var name = "robeem_migtest_" + Guid.NewGuid().ToString("N")[..8];

            await using var connection = new NpgsqlConnection(server.ConnectionString);
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await create.ExecuteNonQueryAsync();
            return new PostgresTestDatabase(server, name);
        }

        public RobeemDbContext CreateContext(string tenantId)
        {
            var connection = new NpgsqlConnectionStringBuilder(_server.ConnectionString) { Database = _name };
            var options = new DbContextOptionsBuilder<RobeemDbContext>().UseNpgsql(connection.ConnectionString).Options;
            return new RobeemDbContext(options, new FixedTenantContext(tenantId));
        }

        /// <summary>The catalog, in the same database (the prime tenant's), as a scope of <paramref name="tenantId"/> sees it.</summary>
        public CatalogDbContext CreateCatalog(string tenantId)
        {
            var connection = new NpgsqlConnectionStringBuilder(_server.ConnectionString) { Database = _name };
            var options = new DbContextOptionsBuilder<CatalogDbContext>()
                .UseNpgsql(connection.ConnectionString, npgsql => npgsql.MigrationsHistoryTable(CatalogDbContext.MigrationsHistoryTable, "Catalog"))
                .Options;
            return new CatalogDbContext(options, new FixedPrimeTenantContext(tenantId == "prime" ? TestDatabase.Prime : tenantId));
        }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var connection = new NpgsqlConnection(_server.ConnectionString);
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
