using robeem.DataModel;

namespace robeem.Tests.Attendance;

/// <summary>
/// An employee paid by the hour is not compared with a schedule: a day without punches is no exception, the hours worked are what counts,
/// and an exception is raised only when those hours cannot be calculated.
/// </summary>
public sealed class AttendanceHourlyTests : AttendanceEngineTestBase
{
    public AttendanceHourlyTests()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        db.Profiles.Single(p => p.Id == EmployeeId).PayBasis = PayBasis.Hourly;
        db.SaveChanges();
    }

    [Fact]
    public async Task ACompleteDay_IsApprovedOnTheHoursWorked_WithNoShiftToCompareWith()
    {
        Clock.Local = Monday.At(23, 0);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(9, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 30));

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((null, TimeSpan.FromHours(8.5), 0, 0, AttendanceRecordStatus.Approved), (record.TimeScheduleShiftId, record.WorkingHours, record.LateMinutes, record.OvertimeMinutes, record.Status));
        Assert.Empty(db.AttendanceExceptions);
    }

    [Fact]
    public async Task ASchedule_EvenOneTheyWereGiven_IsIgnored_AndAMissingDayIsNoException()
    {
        AssignSchedule(StandardDay);
        Clock.Local = Monday.At(23, 0);

        await ProcessAsync(Monday);

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Empty(db.AttendanceExceptions);
            Assert.Empty(db.AttendanceRecords);
        }

        // Worked a different day than the schedule says: the hours are what count, with no lateness, early leave or break rule.
        AddPunch(NormalizedEventType.CheckIn, Monday.At(10, 30));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(15, 0));
        await ProcessAsync(Monday);

        using var check = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(check.AttendanceRecords);
        Assert.Equal((null, TimeSpan.FromHours(4.5), BreakFindings.None, 0, 0, AttendanceRecordStatus.Approved), (record.TimeScheduleShiftId, record.WorkingHours, record.BreakFindings, record.LateMinutes, record.EarlyDepartureMinutes, record.Status));
        Assert.Empty(check.AttendanceExceptions);
    }

    [Fact]
    public async Task ACheckInWithNoCheckOut_IsAnException_OnceTheDayAndTheGraceHavePassed()
    {
        AddPunch(NormalizedEventType.CheckIn, Monday.At(9, 0));

        Clock.Local = Monday.AddDays(1).At(1, 59);
        await ProcessAsync(Monday);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Empty(db.AttendanceExceptions);
        }

        Clock.Local = Monday.AddDays(1).At(3, 0);
        await ProcessAsync(Monday);

        using var check = Database.CreateContext(TestDatabase.TenantA);
        var exception = Assert.Single(check.AttendanceExceptions);
        Assert.Equal((AttendanceExceptionType.MissingPunch, NormalizedEventType.CheckOut), (exception.ExceptionType, exception.MissingEventType!.Value));
        Assert.Equal(AttendanceRecordStatus.PendingReview, Assert.Single(check.AttendanceRecords).Status);
    }

    [Fact]
    public async Task AShiftAcrossMidnight_IsOneSession_OnTheDayOfItsCheckIn_AndTheNextDayIsEmpty()
    {
        var tuesday = Monday.AddDays(1);
        Clock.Local = tuesday.At(12, 0);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(22, 0));
        AddPunch(NormalizedEventType.CheckOut, tuesday.At(6, 0));

        await ProcessAsync(Monday);
        await ProcessAsync(tuesday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords.Where(r => r.IsCurrent));
        Assert.Equal((Monday, TimeSpan.FromHours(8), AttendanceRecordStatus.Approved), (record.AttendanceDate, record.WorkingHours, record.Status));
        Assert.Empty(db.AttendanceExceptions);
    }

    [Fact]
    public async Task AnOnDemandSchedulesOwnPolicy_AppliesToTheHourlyEmployeeOnIt_WhileTheDefaultDoesNot()
    {
        int scheduleId;
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var schedule = new TimeSchedule { Title = "Hourly staff" };
            var team = new TimeScheduleTeam { Title = "All", TimeSchedule = schedule, AllInclusive = true };
            db.Add(team);
            db.OnDemandTimeSchedules.Add(new OnDemandTimeSchedule { TimeSchedule = schedule });
            db.SaveChanges();
            db.TimeScheduleEmployees.Add(new TimeScheduleEmployee { TimeScheduleId = schedule.Id, TeamId = team.Id, EmployeeId = EmployeeId });
            db.SaveChanges();
            scheduleId = schedule.Id;
        }

        // The tenant default allows a session of 16 hours; the on-demand schedule's own policy only 4.
        AddDefaultPolicy(p => p.HourlyMaxSpanHours = 16);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendancePolicies.Add(new AttendancePolicy { Title = "Hourly staff", TimeScheduleId = scheduleId, HourlyMaxSpanHours = 4 });
            db.SaveChanges();
        }

        var tuesday = Monday.AddDays(1);
        Clock.Local = tuesday.At(12, 0);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(22, 0));
        AddPunch(NormalizedEventType.CheckOut, tuesday.At(6, 0));

        await ProcessAsync(Monday);
        await ProcessAsync(tuesday);

        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(2, check.AttendanceExceptions.Count());
        Assert.Empty(check.AttendanceRecords.Where(r => r.IsCurrent && r.Status == AttendanceRecordStatus.Approved));
    }

    [Fact]
    public async Task ACheckOutBeyondThePolicysSpan_IsNotPaired_AndBothAreReported()
    {
        AddDefaultPolicy(p => p.HourlyMaxSpanHours = 4);
        var tuesday = Monday.AddDays(1);
        Clock.Local = tuesday.At(12, 0);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(22, 0));
        AddPunch(NormalizedEventType.CheckOut, tuesday.At(6, 0));

        await ProcessAsync(Monday);
        await ProcessAsync(tuesday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(
            [(Monday, NormalizedEventType.CheckOut), (tuesday, NormalizedEventType.CheckIn)],
            db.AttendanceExceptions.OrderBy(e => e.AttendanceDate).AsEnumerable().Select(e => (e.AttendanceDate, e.MissingEventType!.Value)));
    }
}
