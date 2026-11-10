using robeem.Application.Attendance;
using robeem.DataModel;

namespace robeem.Tests.Attendance;

/// <summary>
/// Reading a break out of the punches of a device that sends only in and out, adjusted for approved leave and for overnight shifts
/// (CLAUDE.md §1.6: interpreted from punches that exist, marked inferred, never invented). The shift is 08:00-17:00 with a break from
/// 12:00 to 13:00; the policy reads the gap near the break.
/// </summary>
public sealed class AttendanceBreakInferenceTests : AttendanceEngineTestBase
{
    public AttendanceBreakInferenceTests()
    {
        AssignSchedule(StandardDay);
        Clock.Local = Monday.At(18, 0);
    }

    [Fact]
    public async Task TheGapNearTheBreak_IsReadAsTheBreak_MarkedInferred_AndNothingIsMissing()
    {
        UseInference();
        AddPunches("I@08:00,O@12:05,I@13:00,O@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((Monday.At(12, 5), AttendancePunchSource.Inferred, Monday.At(13, 0), AttendancePunchSource.Inferred), (record.ActualBreakOut, record.ActualBreakOutSource, record.ActualBreakIn, record.ActualBreakInSource));
        Assert.Equal((AttendancePunchSource.Normalized, AttendancePunchSource.Normalized), (record.ActualCheckInSource, record.ActualCheckOutSource));
        Assert.Equal((TimeSpan.FromMinutes(4 * 60) + TimeSpan.FromMinutes(5) + TimeSpan.FromHours(4), TimeSpan.FromMinutes(55), BreakFindings.None, AttendanceRecordStatus.Approved), (record.WorkingHours, record.BreakDuration, record.BreakFindings, record.Status));
        Assert.Empty(db.AttendanceExceptions);

        // The punches themselves are untouched: the normalized rows keep the types the device sent (CLAUDE.md invariant 7).
        Assert.Equal([NormalizedEventType.CheckIn, NormalizedEventType.CheckOut, NormalizedEventType.CheckIn, NormalizedEventType.CheckOut], db.NormalizedAttendanceTransactions.OrderBy(n => n.EventTimestampUtc).Select(n => n.NormalizedEventType));
    }

    [Fact]
    public async Task WithTheModeOff_TheDayIsAsItWas_TwoWorkSegmentsAndTheBreakReportedMissing()
    {
        AddPunches("I@08:00,O@12:05,I@13:00,O@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Null(record.ActualBreakOut);
        Assert.Equal((BreakFindings.NoBreak, AttendanceRecordStatus.PendingReview), (record.BreakFindings, record.Status));
        Assert.Equal(
            [NormalizedEventType.BreakOut, NormalizedEventType.BreakIn],
            db.AttendanceExceptions.Where(e => e.ExceptionType == AttendanceExceptionType.MissingPunch).Select(e => e.MissingEventType!.Value).AsEnumerable().Order());
    }

    /// <summary>
    /// In order: a gap 150 minutes from the break start (outside the 90-minute window); a gap of 150 minutes (longer than the longest read as a break);
    /// two gaps that fit (ambiguous); an odd number of punches (the check-out is missing, and is not invented); punches that do not alternate.
    /// </summary>
    [Theory]
    [InlineData("I@08:00,O@09:30,I@10:00,O@17:00")]
    [InlineData("I@08:00,O@11:30,I@14:00,O@17:00")]
    [InlineData("I@08:00,O@11:30,I@12:00,O@12:30,I@13:00,O@17:00")]
    [InlineData("I@08:00,O@12:00,I@13:00")]
    [InlineData("I@08:00,I@12:00,O@13:00,O@17:00")]
    public async Task WhenNoSingleGapFits_NothingIsRead(string punches)
    {
        UseInference();
        AddPunches(punches);

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Null(record.ActualBreakOut);
        Assert.Null(record.ActualBreakIn);
        Assert.NotEqual(AttendanceRecordStatus.Approved, record.Status);
    }

    [Fact]
    public async Task AGapShorterThanTheMinimum_IsAShortBreak_ItsTimeStaysWork_AndNoBreakIsInferredFromIt()
    {
        UseInference();
        AddPunches("I@08:00,O@12:00,I@12:05,O@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((TimeSpan.FromHours(9), TimeSpan.Zero, BreakFindings.NoBreak), (record.WorkingHours, record.BreakDuration, record.BreakFindings));
        Assert.Null(record.ActualBreakOut);
    }

    [Fact]
    public async Task PunchesTheDeviceTypedAsBreaks_AreLeftAsTheyAre()
    {
        UseInference();
        AddPunches("I@08:00,BO@12:00,BI@13:00,O@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((AttendancePunchSource.Normalized, AttendancePunchSource.Normalized), (record.ActualBreakOutSource, record.ActualBreakInSource));
    }

    [Fact]
    public async Task WorkOutsideAnyShift_InfersNothing()
    {
        UseInference();
        var tuesday = Monday.AddDays(1);
        Clock.Local = tuesday.At(23, 0);
        AddPunch(NormalizedEventType.CheckIn, tuesday.At(8, 0));
        AddPunch(NormalizedEventType.CheckOut, tuesday.At(12, 0));
        AddPunch(NormalizedEventType.CheckIn, tuesday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, tuesday.At(17, 0));

        await ProcessAsync(tuesday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Null(record.TimeScheduleShiftId);
        Assert.Null(record.ActualBreakOut);
    }

    [Fact]
    public async Task LeaveOverTheGap_MeansItIsNotABreak_NoBreakIsNotMissed_AndTheDayIsComplete()
    {
        UseInference();
        AddLeave(Monday, "12:00", "13:30");
        AddPunches("I@08:00,O@12:00,I@13:30,O@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Null(record.ActualBreakOut);
        Assert.Equal((BreakFindings.None, 30, TimeSpan.FromMinutes(7.5 * 60), 0, AttendanceRecordStatus.Approved), (record.BreakFindings, record.ExcusedMinutes, record.WorkingHours, record.OvertimeMinutes, record.Status));
        Assert.Empty(db.AttendanceExceptions);
    }

    [Fact]
    public async Task LeaveAtTheStart_IsNotLateness_AndAtTheEnd_IsNotAnEarlyDeparture()
    {
        AddLeave(Monday, "08:00", "09:30");
        AddPunches("I@09:30,BO@12:00,BI@13:00,O@17:00");
        await ProcessAsync(Monday);

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var record = Assert.Single(db.AttendanceRecords);
            Assert.Equal((0, 90, AttendanceRecordStatus.Approved), (record.LateMinutes, record.ExcusedMinutes, record.Status));
        }

        // The next Monday: leave at the end.
        var next = Monday.AddDays(7);
        Clock.Local = next.At(18, 0);
        AddLeave(next, "15:30", "17:00");
        foreach (var (type, time) in new[] { (NormalizedEventType.CheckIn, 8), (NormalizedEventType.BreakOut, 12), (NormalizedEventType.BreakIn, 13), (NormalizedEventType.CheckOut, 15) })
        {
            AddPunch(type, next.At(time, type == NormalizedEventType.CheckOut ? 30 : 0));
        }

        await ProcessAsync(next);

        using var check = Database.CreateContext(TestDatabase.TenantA);
        var later = check.AttendanceRecords.Single(r => r.AttendanceDate == next);
        Assert.Equal((0, 90, AttendanceRecordStatus.Approved), (later.EarlyDepartureMinutes, later.ExcusedMinutes, later.Status));
    }

    [Fact]
    public async Task OvertimeIsMeasuredAgainstTheScheduledTimeMinusTheLeave()
    {
        AddLeave(Monday, "15:30", "17:00");
        AddPunches("I@08:00,BO@12:00,BI@13:00,O@16:30");

        await ProcessAsync(Monday);

        // Worked 7.5 hours of a day that expected 8 - 1.5 = 6.5: an hour over. Leaving at 16:30 is not early, for the leave covers it.
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((60, 0, 90), (record.OvertimeMinutes, record.EarlyDepartureMinutes, record.ExcusedMinutes));
    }

    [Fact]
    public async Task LeaveOverTheBreak_DoesNotCountAsTimeTakenFromWork()
    {
        AddLeave(Monday, "12:00", "13:00");
        AddPunches("I@08:00,O@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((0, BreakFindings.None, AttendanceRecordStatus.Approved), (record.ExcusedMinutes, record.BreakFindings, record.Status));
        Assert.Empty(db.AttendanceExceptions);
    }

    [Fact]
    public async Task LeaveEnteredAfterTheDayWasCalculated_MakesANewVersion_WhenTheDayIsCalculatedAgain()
    {
        AddPunches("I@09:30,BO@12:00,BI@13:00,O@17:00");
        await ProcessAsync(Monday);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Equal((90, 1), (Assert.Single(db.AttendanceRecords).LateMinutes, 1));
        }

        AddLeave(Monday, "08:00", "09:30");
        await ProcessAsync(Monday);

        using var check = Database.CreateContext(TestDatabase.TenantA);
        var records = check.AttendanceRecords.OrderBy(r => r.Version).ToList();
        Assert.Equal([(1, false, 90, 0), (2, true, 0, 90)], records.Select(r => (r.Version, r.IsCurrent, r.LateMinutes, r.ExcusedMinutes)));
    }

    [Fact]
    public async Task AnOvernightShift_HasItsBreakFoundAfterMidnight()
    {
        UseInference();
        ReplaceSchedule(new ShiftSpec("22:00", "06:00", "02:00", "03:00"));
        var tuesday = Monday.AddDays(1);
        Clock.Local = tuesday.At(12, 0);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(22, 0));
        AddPunch(NormalizedEventType.CheckOut, tuesday.At(2, 0));
        AddPunch(NormalizedEventType.CheckIn, tuesday.At(3, 0));
        AddPunch(NormalizedEventType.CheckOut, tuesday.At(6, 0));

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords.Where(r => r.IsCurrent));
        Assert.Equal(Monday, record.AttendanceDate);
        Assert.Equal((tuesday.At(2, 0), AttendancePunchSource.Inferred, tuesday.At(3, 0), AttendancePunchSource.Inferred), (record.ActualBreakOut, record.ActualBreakOutSource, record.ActualBreakIn, record.ActualBreakInSource));
        Assert.Equal((TimeSpan.FromHours(7), TimeSpan.FromHours(1), BreakFindings.None, AttendanceRecordStatus.Approved), (record.WorkingHours, record.BreakDuration, record.BreakFindings, record.Status));
        Assert.Empty(db.AttendanceExceptions);
    }

    [Fact]
    public async Task AnOvernightShift_SeesLeaveAfterMidnight()
    {
        ReplaceSchedule(new ShiftSpec("22:00", "06:00", "02:00", "03:00"));
        var tuesday = Monday.AddDays(1);
        Clock.Local = tuesday.At(12, 0);
        AddLeave(tuesday, "04:00", "06:00");
        AddPunch(NormalizedEventType.CheckIn, Monday.At(22, 0));
        AddPunch(NormalizedEventType.BreakOut, tuesday.At(2, 0));
        AddPunch(NormalizedEventType.BreakIn, tuesday.At(3, 0));
        AddPunch(NormalizedEventType.CheckOut, tuesday.At(4, 0));

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords.Where(r => r.IsCurrent));
        Assert.Equal((0, 120, AttendanceRecordStatus.Approved), (record.EarlyDepartureMinutes, record.ExcusedMinutes, record.Status));
    }

    private void UseInference() => AddDefaultPolicy(p => p.BreakInferenceMode = BreakInferenceMode.FromGap);

    /// <summary>Part-day approved leave of the employee on a date, from and to "HH:mm".</summary>
    private void AddLeave(DateTime date, string from, string to)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        db.EmployeeLeaves.Add(new EmployeeLeave { EmployeeId = EmployeeId, LeaveType = LeaveType.Other, StartDate = date, EndDate = date, IsPartDay = true, StartTime = from, EndTime = to });
        db.SaveChanges();
    }

    /// <summary>Gives the shift of the constructor other times (an overnight shift, say).</summary>
    private void ReplaceSchedule(ShiftSpec spec)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var shift = db.TimeScheduleShifts.Single();
        (shift.StartTime, shift.EndTime, shift.BreakTime, shift.BreakStartTime, shift.BreakEndTime) = (spec.Start, spec.End, true, spec.BreakStart!, spec.BreakEnd!);
        db.SaveChanges();
    }

    private void AddPunches(string punches)
    {
        foreach (var punch in punches.Split(','))
        {
            var parts = punch.Split('@');
            var type = parts[0] switch
            {
                "I" => NormalizedEventType.CheckIn,
                "O" => NormalizedEventType.CheckOut,
                "BO" => NormalizedEventType.BreakOut,
                "BI" => NormalizedEventType.BreakIn,
                _ => throw new ArgumentException($"Unknown punch type {parts[0]}."),
            };
            AddPunch(type, Monday.At(int.Parse(parts[1][..2]), int.Parse(parts[1][3..])));
        }
    }
}
