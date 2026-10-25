using robeem.Application.Attendance;
using robeem.Application.Security;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// One tenant with one employee, one device and a clock, plus helpers to give the employee a
/// schedule and punches. The processing engine and exception tests build on it.
/// </summary>
public abstract class AttendanceEngineTestBase : IDisposable
{
    // 2026-10-05 is a Monday; the schedules below start their cycle on Monday.
    protected static readonly DateTime Monday = new(2026, 10, 5);

    private readonly int _integrationId;
    private readonly int _deviceId;
    private readonly int _rawId;

    protected AttendanceEngineTestBase()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var employee = new Profile { EmployeeNumber = "E-1", FirstName = "Ada", LastName = "Lovelace", Title = "Ada Lovelace" };
        var integration = new AttendanceIntegration
        {
            Connector = AttendanceTestData.Connector("generic-csv", "Generic CSV", adapterKey: "generic-csv"),
            DisplayName = "HQ",
        };
        var device = new AttendanceDevice { Integration = integration, ExternalDeviceId = "DEV-1", Name = "Front door", TimeZoneId = "UTC" };
        var raw = new RawAttendanceTransaction
        {
            Integration = integration,
            ImportBatch = new AttendanceProcessingBatch { Integration = integration, BatchType = AttendanceBatchType.ManualUpload },
            TransactionTimestampRaw = "-",
            RawPayload = "{}",
            SourceSystem = "generic-csv",
            RecordHash = "hash",
        };
        db.AddRange(employee, device, raw);
        db.SaveChanges();

        EmployeeId = employee.Id;
        _integrationId = integration.Id;
        _deviceId = device.Id;
        _rawId = raw.Id;
    }

    // The study's standard day, as implied by Scenarios 1-3: 08:00-17:00 with a 12:00-13:00 break (8 hours).
    protected static ShiftSpec StandardDay => new("08:00", "17:00", "12:00", "13:00");

    protected TestDatabase Database { get; } = new();

    protected int EmployeeId { get; }

    /// <summary>Gets the engine's clock. It starts at midnight on <see cref="Monday"/>: before any shift of the tests has started, so no missing punch is due yet.</summary>
    protected FixedTimeProvider Clock { get; } = new(Monday);

    public void Dispose() => Database.Dispose();

    protected async Task<DayProcessingResult> ProcessAsync(DateTime date)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await CreateEngine(db).ProcessEmployeeDayAsync(EmployeeId, date, CancellationToken.None);
    }

    protected AttendanceProcessingEngine CreateEngine(RobeemDbContext db) =>
        new(db, new ScheduleResolutionService(db), new AttendancePolicyResolver(db), CreateAudit(db), Clock);

    /// <summary>The audit trail of a context, for the system unless a test names the user.</summary>
    protected AttendanceAuditTrail CreateAudit(RobeemDbContext db, ICurrentUser? user = null) =>
        new(db, user ?? new SystemCurrentUser(), Clock);

    /// <summary>A Monday-only schedule with the given shifts, its employee assigned to its all-inclusive team.</summary>
    protected void AssignSchedule(params ShiftSpec[] specs)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var schedule = new TimeSchedule { Title = "Test", CyclePeriod = 7, FirstCycleDay = DayOfWeek.Monday };
        var team = new TimeScheduleTeam { Title = "All", TimeSchedule = schedule, AllInclusive = true };
        db.Add(team);
        foreach (var spec in specs)
        {
            var shift = new TimeScheduleShift
            {
                Title = $"{spec.Start}-{spec.End}",
                TimeSchedule = schedule,
                StartTime = spec.Start,
                EndTime = spec.End,
                BreakTime = spec.BreakStart is not null,
                BreakStartTime = spec.BreakStart ?? "00:00",
                BreakEndTime = spec.BreakEnd ?? "00:00",
            };
            db.TimeScheduleLines.Add(new TimeScheduleLine { TimeSchedule = schedule, Team = team, Shift = shift, CycleDay = 1 });
        }

        db.SaveChanges();
        db.TimeScheduleEmployees.Add(new TimeScheduleEmployee { TimeScheduleId = schedule.Id, TeamId = team.Id, EmployeeId = EmployeeId });
        db.SaveChanges();
    }

    protected void AddDefaultPolicy(Action<AttendancePolicy> configure)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var policy = new AttendancePolicy { Title = "Default" };
        configure(policy);
        db.AttendancePolicies.Add(policy);
        db.SaveChanges();
    }

    /// <summary>A Unique normalized punch from a device in UTC, so local and UTC times coincide.</summary>
    protected void AddPunch(NormalizedEventType type, DateTime local)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var utc = new DateTimeOffset(local, TimeSpan.Zero);
        db.NormalizedAttendanceTransactions.Add(new NormalizedAttendanceTransaction
        {
            EmployeeId = EmployeeId,
            DeviceId = _deviceId,
            IntegrationId = _integrationId,
            RawTransactionId = _rawId,
            AttendanceDate = local.Date,
            EventTimestampUtc = utc,
            EventTimestampLocal = local,
            TimeZoneId = "UTC",
            NormalizedEventType = type,
            ConfidenceStatus = type == NormalizedEventType.Unknown ? EventConfidenceStatus.Ambiguous : EventConfidenceStatus.Certain,
            DuplicateBucketTimestampUtc = utc,
        });
        db.SaveChanges();
    }

    protected sealed record ShiftSpec(string Start, string End, string? BreakStart = null, string? BreakEnd = null);
}
