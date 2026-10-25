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
    }

    [PostgresFact]
    public async Task TheSeed_RunsOnPostgres_AndTwiceChangesNothing()
    {
        await MigrateAsync();

        for (var run = 0; run < 2; run++)
        {
            await using var db = _database!.CreateContext("tenant-a");
            await new SeedDataRunner(db, new ISeedContributor[] { new HrSeedContributor(), new ProfileSeedContributor(Microsoft.Extensions.Options.Options.Create(new SampleDataOptions { TenantIds = ["tenant-a"] })), new AttendanceSeedContributor() }, NullLogger<SeedDataRunner>.Instance)
                .RunAsync(CancellationToken.None);
        }

        await using var check = _database!.CreateContext("tenant-a");
        Assert.Single(check.TimeSchedules);
        Assert.Equal(4, check.AttendanceVendors.Count());
        Assert.Equal(4, check.AttendanceConnectorDefinitions.Count());
        Assert.Single(check.AttendancePolicies);
    }

    [PostgresFact]
    public async Task TheSeed_LinksTheBuiltInFormatsAndKeepsWhatWasThere_OnPostgres()
    {
        await MigrateAsync();
        await using (var db = _database!.CreateContext("tenant-a"))
        {
            // A database from before the formats were data: a vendor with no format, and a device type of the tenant's own.
            var connector = AttendanceTestData.Connector("zkteco", "ZKTeco");
            var integration = new AttendanceIntegration { Connector = connector, DisplayName = "Uploads" };
            db.AttendanceDevices.Add(new AttendanceDevice { Integration = integration, ExternalDeviceId = "1", Name = "A", TimeZoneId = "UTC", DeviceType = new AttendanceDeviceType { Name = "Facial", IsActive = true } });
            await db.SaveChangesAsync();
        }

        for (var run = 0; run < 2; run++)
        {
            await using var db = _database!.CreateContext("tenant-a");
            await new SeedDataRunner(db, new ISeedContributor[] { new AttendanceSeedContributor() }, NullLogger<SeedDataRunner>.Instance)
                .RunAsync(CancellationToken.None);
        }

        await using var check = _database!.CreateContext("tenant-a");
        var zkteco = await check.AttendanceConnectorDefinitions.Include(c => c.Transports).ThenInclude(t => t.Transport).Include(c => c.DefaultPayloadFormat).SingleAsync(c => c.ConnectorKey == "zkteco");
        Assert.Empty(zkteco.Transports);
        Assert.Equal("ZKTeco attendance export", zkteco.DefaultPayloadFormat!.Name);
        Assert.Equal("ZKTeco attendance export", (await check.AttendanceIntegrations.Include(i => i.PayloadFormat).SingleAsync()).PayloadFormat!.Name);
        var device = await check.AttendanceDevices.Include(d => d.DeviceType).SingleAsync();
        Assert.Equal("Facial", device.DeviceType!.Name);
        Assert.Equal(6, await check.AttendanceDeviceTypes.CountAsync());
        Assert.Equal(6, await check.AttendanceTransportDefinitions.CountAsync());
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

        await MigrateAsync();

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
        Assert.False(check.Database.HasPendingModelChanges());
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

        await MigrateAsync();

        await using var check = _database!.CreateContext("tenant-a");
        async Task<List<string>> Strings(string sql) => await check.Database.SqlQueryRaw<string>(sql).ToListAsync();

        // One default connection per integration, with what the integration had; the authentication is derived.
        string[] expected =
        [
            "Integration of csv|Default|True|FileUpload|None|ref-csv|",
            "Integration of hook|Default|True|Webhook|HmacSignature|ref-hook|",
            "Integration of suprema-biostar|Default|True|RestApi|SessionLogin|ref-suprema-biostar|*/5 * * * *",
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
        Assert.False(check.Database.HasPendingModelChanges());
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

        await MigrateAsync();

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
        Assert.False(check.Database.HasPendingModelChanges());
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

        await MigrateAsync();

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
        Assert.False(check.Database.HasPendingModelChanges());
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

    private async Task MigrateAsync()
    {
        await using var db = _database!.CreateContext("tenant-a");
        await db.Database.MigrateAsync();
    }

    private async Task<Fixture> SeedFixtureAsync()
    {
        await using var db = _database!.CreateContext("tenant-a");
        var employee = new Profile { EmployeeNumber = "E-1", FirstName = "Ada", LastName = "Lovelace", Title = "Ada Lovelace" };
        var integration = new AttendanceIntegration
        {
            Connector = AttendanceTestData.Connector("generic-csv", "Generic CSV"),
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
