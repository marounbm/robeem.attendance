using robeem.Application.Attendance;
using robeem.DataModel;

namespace robeem.Tests.Attendance;

/// <summary>
/// Appendix B scenarios 1, 2 and 5 of the study, plus versioning and multi-shift rollup. The study
/// itself is not in the repository: the scenario inputs below are reconstructed so that they give
/// exactly the outcomes the prompt quotes (see ATTENDANCE_P7_NOTES.md).
/// </summary>
public sealed class AttendanceProcessingEngineTests : AttendanceEngineTestBase
{
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
        using var db = Database.CreateContext(TestDatabase.TenantA);
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

        using var db = Database.CreateContext(TestDatabase.TenantA);
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

        using var db = Database.CreateContext(TestDatabase.TenantA);
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

        using var db = Database.CreateContext(TestDatabase.TenantA);
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
        using var db = Database.CreateContext(TestDatabase.TenantA);
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

        using var db = Database.CreateContext(TestDatabase.TenantA);
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

        using var db = Database.CreateContext(TestDatabase.TenantA);
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

        using var db = Database.CreateContext(TestDatabase.TenantA);
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

        using var db = Database.CreateContext(TestDatabase.TenantA);
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

        using var db = Database.CreateContext(TestDatabase.TenantA);
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

        using var db = Database.CreateContext(TestDatabase.TenantA);
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

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = db.AttendanceRecords.Single();
        Assert.Equal(TimeSpan.FromHours(8), record.WorkingHours);
        Assert.Equal(TimeSpan.FromHours(1), record.BreakDuration);
        Assert.All(
            new[] { record.ActualCheckInSource, record.ActualBreakOutSource, record.ActualBreakInSource, record.ActualCheckOutSource },
            s => Assert.Equal(AttendancePunchSource.Inferred, s));

        // The normalized rows themselves are not rewritten.
        Assert.All(db.NormalizedAttendanceTransactions, n => Assert.Equal(NormalizedEventType.Unknown, n.NormalizedEventType));
    }

    private void AddFullStandardDay(DateTime day)
    {
        AddPunch(NormalizedEventType.CheckIn, day.At(8, 0));
        AddPunch(NormalizedEventType.BreakOut, day.At(12, 0));
        AddPunch(NormalizedEventType.BreakIn, day.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, day.At(17, 0));
    }
}
