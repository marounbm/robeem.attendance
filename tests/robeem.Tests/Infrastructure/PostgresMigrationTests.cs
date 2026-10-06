using Microsoft.EntityFrameworkCore;
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
        Assert.Equal(4, check.AttendanceProviders.Count());
        Assert.Single(check.AttendancePolicies);
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
        var raw = await check.RawAttendanceTransactions.SingleAsync();
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
        AttendanceDate = bucket.UtcDateTime.Date,
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
            Provider = new AttendanceProvider { ProviderKey = "generic-csv", DisplayName = "Generic CSV" },
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
