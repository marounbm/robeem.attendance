using robeem.Application.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// Appendix B scenarios 1, 2 and 5 of the study, plus versioning and multi-shift rollup. The study
/// itself is not in the repository: the scenario inputs below are reconstructed so that they give
/// exactly the outcomes the prompt quotes (see ATTENDANCE_P7_NOTES.md).
/// </summary>
public sealed class AttendanceProcessingEngineTests : IDisposable
{
    // 2026-10-05 is a Monday; the schedules below start their cycle on Monday.
    private static readonly DateTime Monday = new(2026, 10, 5);

    private readonly TestDatabase _database = new();
    private readonly int _employeeId;
    private readonly int _integrationId;
    private readonly int _deviceId;
    private readonly int _rawId;

    public AttendanceProcessingEngineTests()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var employee = new Profile { EmployeeNumber = "E-1", FirstName = "Ada", LastName = "Lovelace", Title = "Ada Lovelace" };
        var integration = new AttendanceIntegration
        {
            Provider = new AttendanceProvider { ProviderKey = "generic-csv", DisplayName = "Generic CSV" },
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

        _employeeId = employee.Id;
        _integrationId = integration.Id;
        _deviceId = device.Id;
        _rawId = raw.Id;
    }

    // The study's standard day, as implied by Scenarios 1-3: 08:00-17:00 with a 12:00-13:00 break (8 hours).
    private static ShiftSpec StandardDay => new("08:00", "17:00", "12:00", "13:00");

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Scenario1_StandardDay()
    {
        AssignSchedule(StandardDay);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));

        var result = await ProcessAsync(Monday);

        Assert.Equal(new DayProcessingResult(1, 0, 0), result);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var record = db.AttendanceRecords.Single();
        Assert.Equal(Monday, record.AttendanceDate);
        Assert.Equal((Monday.At(8, 0), Monday.At(17, 0)), (record.ScheduledStart, record.ScheduledEnd));
        Assert.Equal((Monday.At(8, 0), Monday.At(12, 0), Monday.At(13, 0), Monday.At(17, 0)), (record.ActualCheckIn, record.ActualBreakOut, record.ActualBreakIn, record.ActualCheckOut));
        Assert.Equal(AttendancePunchSource.Normalized, record.ActualCheckInSource);
        Assert.Equal(TimeSpan.FromHours(8), record.WorkingHours);
        Assert.Equal(TimeSpan.FromHours(1), record.BreakDuration);
        Assert.Equal((0, 0, 0), (record.LateMinutes, record.EarlyDepartureMinutes, record.OvertimeMinutes));
        Assert.Equal(AttendanceRecordStatus.Approved, record.Status);
        Assert.Equal((1, true), (record.Version, record.IsCurrent));
        Assert.Equal(4, record.SourceNormalizedTransactionIds!.Split(',').Length);

        var summary = db.AttendanceDaySummaries.Single();
        Assert.Equal((TimeSpan.FromHours(8), TimeSpan.Zero, TimeSpan.FromHours(1)), (summary.TotalWorkingHours, summary.TotalOvertimeHours, summary.TotalBreakHours));
        Assert.Equal((1, AttendanceRecordStatus.Approved), (summary.ShiftCount, summary.Status));
    }

    [Fact]
    public async Task Scenario2_LateArrivalWithOvertime_NeedsReviewWhenOvertimeRequiresPreApproval()
    {
        AssignSchedule(StandardDay);
        AddDefaultPolicy(p => p.RequiresOvertimePreApproval = true);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 12));
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(18, 35));

        await ProcessAsync(Monday);

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var record = db.AttendanceRecords.Single();
        Assert.Equal(12, record.LateMinutes);
        Assert.Equal(0, record.EarlyDepartureMinutes);
        Assert.Equal(new TimeSpan(9, 23, 0), record.WorkingHours);
        Assert.Equal(83, record.OvertimeMinutes); // 1:23
        Assert.Equal(AttendanceRecordStatus.PendingReview, record.Status);

        var summary = db.AttendanceDaySummaries.Single();
        Assert.Equal(new TimeSpan(1, 23, 0), summary.TotalOvertimeHours);
        Assert.Equal(AttendanceRecordStatus.PendingReview, summary.Status);
    }

    [Fact]
    public async Task Scenario2_WithoutPreApprovalRequirement_IsApproved()
    {
        AssignSchedule(StandardDay);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 12));
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(18, 35));

        await ProcessAsync(Monday);

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var record = db.AttendanceRecords.Single();
        Assert.Equal((83, AttendanceRecordStatus.Approved), (record.OvertimeMinutes, record.Status));
    }

    [Fact]
    public async Task Scenario5_OvernightShift_IsDatedOnItsStart_AndWorkedAcrossMidnight()
    {
        AssignSchedule(new ShiftSpec("22:00", "06:00"));
        var tuesday = Monday.AddDays(1);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(22, 0));
        AddPunch(NormalizedEventType.CheckOut, tuesday.At(6, 0));

        await ProcessAsync(Monday);

        // Processing Tuesday must not claim the 06:00 check-out: Monday's shift owns it.
        Assert.Equal(new DayProcessingResult(0, 0, 0), await ProcessAsync(tuesday));

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var record = db.AttendanceRecords.Single();
        Assert.Equal(Monday, record.AttendanceDate);
        Assert.Equal((Monday.At(22, 0), tuesday.At(6, 0)), (record.ScheduledStart, record.ScheduledEnd));
        Assert.Equal(tuesday.At(6, 0), record.ActualCheckOut);
        Assert.Equal(TimeSpan.FromHours(8), record.WorkingHours);
        Assert.Equal((0, 0, 0), (record.LateMinutes, record.EarlyDepartureMinutes, record.OvertimeMinutes));
        Assert.Equal(AttendanceRecordStatus.Approved, record.Status);
        Assert.Equal(Monday, db.AttendanceDaySummaries.Single().AttendanceDate);
    }

    [Fact]
    public async Task Reprocessing_AnUnchangedDay_KeepsTheRecord()
    {
        AssignSchedule(StandardDay);
        AddFullStandardDay(Monday);
        await ProcessAsync(Monday);

        var again = await ProcessAsync(Monday);

        Assert.Equal(new DayProcessingResult(0, 1, 0), again);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(1, Assert.Single(db.AttendanceRecords).Version);
    }

    [Fact]
    public async Task Reprocessing_AChangedDay_WritesANewVersion_AndRetiresThePreviousOne()
    {
        AssignSchedule(StandardDay);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        await ProcessAsync(Monday);

        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));
        Assert.Equal(new DayProcessingResult(1, 0, 0), await ProcessAsync(Monday));

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var versions = db.AttendanceRecords.OrderBy(r => r.Version).ToList();
        Assert.Equal(2, versions.Count);

        // Version 1 is kept, as calculated: the check-out was missing, so it needed review.
        Assert.False(versions[0].IsCurrent);
        Assert.Equal(versions[1].Id, versions[0].SupersededByRecordId);
        Assert.Equal(AttendanceRecordStatus.PendingReview, versions[0].Status);
        Assert.Equal(TimeSpan.FromHours(4), versions[0].WorkingHours);

        Assert.True(versions[1].IsCurrent);
        Assert.Equal((TimeSpan.FromHours(8), AttendanceRecordStatus.Approved), (versions[1].WorkingHours, versions[1].Status));

        var summary = db.AttendanceDaySummaries.Single();
        Assert.Equal((TimeSpan.FromHours(8), 1, AttendanceRecordStatus.Approved), (summary.TotalWorkingHours, summary.ShiftCount, summary.Status));
    }

    [Fact]
    public async Task MultiShiftDay_GivesOneRecordPerShift_AndOneSummaryOfThemAll()
    {
        AssignSchedule(new ShiftSpec("07:00", "11:00"), new ShiftSpec("17:00", "21:00"));
        AddPunch(NormalizedEventType.CheckIn, Monday.At(7, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(11, 0));
        AddPunch(NormalizedEventType.CheckIn, Monday.At(17, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(21, 30));

        Assert.Equal(new DayProcessingResult(2, 0, 0), await ProcessAsync(Monday));

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var records = db.AttendanceRecords.OrderBy(r => r.ScheduledStart).ToList();
        Assert.Equal([TimeSpan.FromHours(4), new TimeSpan(4, 30, 0)], records.Select(r => r.WorkingHours));
        Assert.Equal([0, 30], records.Select(r => r.OvertimeMinutes));

        var summary = db.AttendanceDaySummaries.Single();
        Assert.Equal(new TimeSpan(8, 30, 0), summary.TotalWorkingHours);
        Assert.Equal(TimeSpan.FromMinutes(30), summary.TotalOvertimeHours);
        Assert.Equal(2, summary.ShiftCount);
    }

    [Fact]
    public async Task GracePeriod_IsSubtractedFromLateness()
    {
        AssignSchedule(StandardDay);
        AddDefaultPolicy(p => p.LateGraceMinutes = 5);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 12));
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));

        await ProcessAsync(Monday);

        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(7, db.AttendanceRecords.Single().LateMinutes);
    }

    [Fact]
    public async Task Rounding_AppliesToTheCalculation_NotToTheRecordedPunches()
    {
        AssignSchedule(StandardDay);
        AddDefaultPolicy(p => p.RoundingRuleMinutes = 15);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 7));
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(16, 53));

        await ProcessAsync(Monday);

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var record = db.AttendanceRecords.Single();
        Assert.Equal((Monday.At(8, 7), Monday.At(16, 53)), (record.ActualCheckIn, record.ActualCheckOut));
        Assert.Equal(TimeSpan.FromHours(8), record.WorkingHours);
        Assert.Equal((0, 0), (record.LateMinutes, record.EarlyDepartureMinutes));
    }

    [Fact]
    public async Task MissingBreakPunches_OnAShiftWithABreak_AreNotInvented_AndNeedReview()
    {
        AssignSchedule(StandardDay);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));

        await ProcessAsync(Monday);

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var record = db.AttendanceRecords.Single();
        Assert.Equal(TimeSpan.FromHours(9), record.WorkingHours);
        Assert.Null(record.ActualBreakOut);
        Assert.Equal(0, record.OvertimeMinutes);
        Assert.Equal(AttendanceRecordStatus.PendingReview, record.Status);
    }

    [Fact]
    public async Task WorkOnADayOff_IsRecordedWithoutAShift_ForReview()
    {
        AssignSchedule(StandardDay);
        var sunday = Monday.AddDays(6);
        AddPunch(NormalizedEventType.CheckIn, sunday.At(10, 0));
        AddPunch(NormalizedEventType.CheckOut, sunday.At(12, 0));

        await ProcessAsync(sunday);

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var record = db.AttendanceRecords.Single();
        Assert.Null(record.TimeScheduleShiftId);
        Assert.Equal(TimeSpan.FromHours(2), record.WorkingHours);
        Assert.Equal(AttendanceRecordStatus.PendingReview, record.Status);
    }

    [Fact]
    public async Task UnlabeledPunches_AreInferredSequentially_WhenThePolicySaysSo_AndMarkedInferred()
    {
        AssignSchedule(StandardDay);
        AddDefaultPolicy(p => p.UnlabeledPunchInterpretationMode = UnlabeledPunchInterpretationMode.Sequential);
        AddPunch(NormalizedEventType.Unknown, Monday.At(8, 0));
        AddPunch(NormalizedEventType.Unknown, Monday.At(12, 0));
        AddPunch(NormalizedEventType.Unknown, Monday.At(13, 0));
        AddPunch(NormalizedEventType.Unknown, Monday.At(17, 0));

        await ProcessAsync(Monday);

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var record = db.AttendanceRecords.Single();
        Assert.Equal(TimeSpan.FromHours(8), record.WorkingHours);
        Assert.Equal(TimeSpan.FromHours(1), record.BreakDuration);
        Assert.All(
            new[] { record.ActualCheckInSource, record.ActualBreakOutSource, record.ActualBreakInSource, record.ActualCheckOutSource },
            s => Assert.Equal(AttendancePunchSource.Inferred, s));

        // The normalized rows themselves are not rewritten.
        Assert.All(db.NormalizedAttendanceTransactions, n => Assert.Equal(NormalizedEventType.Unknown, n.NormalizedEventType));
    }

    private async Task<DayProcessingResult> ProcessAsync(DateTime date)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await CreateEngine(db).ProcessEmployeeDayAsync(_employeeId, date, CancellationToken.None);
    }

    private AttendanceProcessingEngine CreateEngine(RobeemDbContext db) =>
        new(db, new ScheduleResolutionService(db), new AttendancePolicyResolver(db));

    /// <summary>A Monday-only schedule with the given shifts, its employee assigned to its all-inclusive team.</summary>
    private void AssignSchedule(params ShiftSpec[] specs)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
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
        db.TimeScheduleEmployees.Add(new TimeScheduleEmployee { TimeScheduleId = schedule.Id, TeamId = team.Id, EmployeeId = _employeeId });
        db.SaveChanges();
    }

    private void AddDefaultPolicy(Action<AttendancePolicy> configure)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var policy = new AttendancePolicy { Title = "Default" };
        configure(policy);
        db.AttendancePolicies.Add(policy);
        db.SaveChanges();
    }

    private void AddFullStandardDay(DateTime day)
    {
        AddPunch(NormalizedEventType.CheckIn, day.At(8, 0));
        AddPunch(NormalizedEventType.BreakOut, day.At(12, 0));
        AddPunch(NormalizedEventType.BreakIn, day.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, day.At(17, 0));
    }

    /// <summary>A Unique normalized punch from a device in UTC, so local and UTC times coincide.</summary>
    private void AddPunch(NormalizedEventType type, DateTime local)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var utc = new DateTimeOffset(local, TimeSpan.Zero);
        db.NormalizedAttendanceTransactions.Add(new NormalizedAttendanceTransaction
        {
            EmployeeId = _employeeId,
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

    private sealed record ShiftSpec(string Start, string End, string? BreakStart = null, string? BreakEnd = null);
}
