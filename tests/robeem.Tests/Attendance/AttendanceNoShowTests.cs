using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.DataModel;

namespace robeem.Tests.Attendance;

/// <summary>
/// A scheduled shift nobody punched for is one no-show exception, unless a leave or holiday covers the day. It is settled by punches
/// arriving, by a manager or HR accepting it, or by an absence entered later. The shift is 08:00-17:00 with a break from 12:00 to 13:00, on
/// Mondays only; the missing-punch grace is two hours, so a Monday no-show is due from 19:00.
/// </summary>
public sealed class AttendanceNoShowTests : AttendanceEngineTestBase
{
    private static readonly DateTime Due = Monday.At(19, 0);

    [Fact]
    public async Task ANoShow_IsOneException_RaisedOnceTheGraceHasElapsed_AndNotBefore_AndOnlyOnce()
    {
        AssignSchedule(StandardDay);

        Clock.Local = Due.AddMinutes(-1);
        await ProcessAsync(Monday);
        Assert.Empty(Exceptions());

        Clock.Local = Due;
        await ProcessAsync(Monday);
        await ProcessAsync(Monday);

        var noShow = Assert.Single(Exceptions());
        Assert.Equal((AttendanceExceptionType.NoShow, AttendanceExceptionStatus.Open, null), (noShow.ExceptionType, noShow.Status, noShow.MissingEventType));
        Assert.StartsWith("NoShow — ", noShow.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADayOff_RaisesNothing()
    {
        AssignSchedule(StandardDay);
        var tuesday = Monday.AddDays(1);
        Clock.Local = tuesday.At(23, 0);

        await ProcessAsync(tuesday);

        Assert.Empty(Exceptions());
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceRecords);
    }

    [Fact]
    public async Task AnEmployeeWithNoSchedule_RaisesNothing()
    {
        Clock.Local = Due.AddHours(4);

        await ProcessAsync(Monday);

        Assert.Empty(Exceptions());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ADayCoveredByAHolidayOrALeave_FromTheStart_RaisesNothing_AndWritesNoRecord(bool holiday)
    {
        AssignSchedule(StandardDay);
        AddAbsence(holiday);
        Clock.Local = Due.AddHours(1);

        await ProcessAsync(Monday);

        Assert.Empty(Exceptions());
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceRecords);
        Assert.Empty(db.AttendanceApprovals);
    }

    [Fact]
    public async Task AHolidayEnteredLater_ClosesTheOpenNoShow_AsExcused_AndRemovingItBringsANewOneBack()
    {
        AssignSchedule(StandardDay);
        Clock.Local = Due.AddHours(1);
        await ProcessAsync(Monday);
        Assert.Equal(AttendanceExceptionStatus.Open, Assert.Single(Exceptions()).Status);

        AddAbsence(holiday: true);
        await ProcessAsync(Monday);

        var excused = Assert.Single(Exceptions());
        Assert.Equal((AttendanceExceptionStatus.Closed, AttendanceExceptionResolutionMethod.ExcusedAbsence), (excused.Status, excused.ResolutionMethod));
        Assert.Contains("Holiday: Labour Day", excused.ResolutionNote);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            // The record that waited for review is retired with the exception; there is nothing to approve.
            Assert.Empty(db.AttendanceRecords.Where(r => r.IsCurrent));
            Assert.DoesNotContain(db.AttendanceApprovals, a => a.Status == AttendanceApprovalStatus.Pending);
            Assert.Contains(db.AttendanceAuditLogs, a => a.Action == AttendanceAuditActions.ExceptionStatusChanged && a.Details!.Contains("excused by approved absence"));
        }

        // The holiday is taken away again: the day is a no-show once more, as a new exception. The excused one stays as history.
        RemoveAbsences();
        await ProcessAsync(Monday);

        Assert.Equal(
            [(AttendanceExceptionStatus.Closed, AttendanceExceptionResolutionMethod.ExcusedAbsence), (AttendanceExceptionStatus.Open, (AttendanceExceptionResolutionMethod?)null)],
            Exceptions().Select(e => (e.Status, e.ResolutionMethod)));
    }

    [Fact]
    public async Task APartDayLeave_DoesNotExcuseANoShow()
    {
        AssignSchedule(StandardDay);
        AddPartDayLeave("08:00", "10:00");
        Clock.Local = Due.AddHours(1);

        await ProcessAsync(Monday);

        Assert.Equal(AttendanceExceptionType.NoShow, Assert.Single(Exceptions()).ExceptionType);
    }

    [Fact]
    public async Task PunchesArrivingLate_ResolveTheNoShow_Automatically()
    {
        AssignSchedule(StandardDay);
        Clock.Local = Due.AddHours(1);
        await ProcessAsync(Monday);

        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));
        await ProcessAsync(Monday);

        var resolved = Assert.Single(Exceptions());
        Assert.Equal((AttendanceExceptionStatus.Closed, AttendanceExceptionResolutionMethod.AutoResolved), (resolved.Status, resolved.ResolutionMethod));
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceRecordStatus.Approved, Assert.Single(db.AttendanceRecords.Where(r => r.IsCurrent)).Status);
    }

    [Fact]
    public async Task AManagerOrHr_CanAcceptANoShowWithAReason_AndItIsNotRaisedAgain_ButTheEmployeeCannot()
    {
        var managerId = AddManager();
        AssignSchedule(StandardDay);
        Clock.Local = Due.AddHours(1);
        await ProcessAsync(Monday);
        var id = Assert.Single(Exceptions()).Id;

        var byEmployee = await AcceptAsync(id, EmployeeId, asHr: false, "I was ill.");
        var withoutReason = await AcceptAsync(id, managerId, asHr: false, " ");
        var accepted = await AcceptAsync(id, managerId, asHr: false, "Ill, confirmed by phone.");
        await ProcessAsync(Monday);

        Assert.False(byEmployee.Succeeded);
        Assert.False(withoutReason.Succeeded);
        Assert.True(accepted.Succeeded, string.Join("; ", accepted.Errors.Values));
        var only = Assert.Single(Exceptions());
        Assert.Equal((AttendanceExceptionStatus.Closed, AttendanceExceptionResolutionMethod.Accepted, managerId, "Ill, confirmed by phone."), (only.Status, only.ResolutionMethod, only.ResolvedById, only.ResolutionNote));
    }

    [Fact]
    public async Task TheDayOfANoShow_CanBeCorrectedByTheEmployee()
    {
        AssignSchedule(StandardDay);
        Clock.Local = Due.AddHours(1);
        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var day = await new AttendanceDayCorrectionService(db, new ScheduleResolutionService(db), new AttendanceExceptionQuery(db), new AttendancePolicyResolver(db), CreateAudit(db), Clock)
            .GetDayAsync(EmployeeId, Monday, EmployeeId, CancellationToken.None);

        Assert.True(day!.CanCorrect, day.CannotCorrectReason);
    }

    [Fact]
    public async Task AnOvernightShiftOfTheDayBefore_ClaimsItsMorningPunches_SoTheNextDayIsNeitherUnscheduledWorkNorAMissingDay()
    {
        AssignSchedule(new ShiftSpec("22:00", "06:00"));
        var tuesday = Monday.AddDays(1);
        Clock.Local = tuesday.At(12, 0);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(22, 0));
        AddPunch(NormalizedEventType.CheckOut, tuesday.At(6, 0));

        await ProcessAsync(Monday);
        await ProcessAsync(tuesday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords.Where(r => r.IsCurrent));
        Assert.Equal((Monday, AttendanceRecordStatus.Approved), (record.AttendanceDate, record.Status));
        Assert.Empty(db.AttendanceExceptions);
    }

    private List<AttendanceException> Exceptions()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return db.AttendanceExceptions.OrderBy(e => e.Id).ToList();
    }

    private void AddAbsence(bool holiday)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        if (holiday)
        {
            db.PublicHolidays.Add(new PublicHoliday { Date = Monday, Title = "Labour Day" });
        }
        else
        {
            db.EmployeeLeaves.Add(new EmployeeLeave { EmployeeId = EmployeeId, LeaveType = LeaveType.Sick, StartDate = Monday, EndDate = Monday });
        }

        db.SaveChanges();
    }

    private void AddPartDayLeave(string from, string to)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        db.EmployeeLeaves.Add(new EmployeeLeave { EmployeeId = EmployeeId, LeaveType = LeaveType.Other, StartDate = Monday, EndDate = Monday, IsPartDay = true, StartTime = from, EndTime = to });
        db.SaveChanges();
    }

    private void RemoveAbsences()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        db.PublicHolidays.RemoveRange(db.PublicHolidays);
        db.EmployeeLeaves.RemoveRange(db.EmployeeLeaves);
        db.SaveChanges();
    }

    private int AddManager()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var manager = new Profile { EmployeeNumber = "M-1", FirstName = "Grace", LastName = "Hopper", Title = "Grace Hopper" };
        db.Add(manager);
        db.SaveChanges();
        db.Profiles.Single(p => p.Id == EmployeeId).ManagerId = manager.Id;
        db.SaveChanges();
        return manager.Id;
    }

    private async Task<SaveResult> AcceptAsync(int exceptionId, int by, bool asHr, string reason)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await new AttendanceExceptionService(db, CreateEngine(db), CreateAudit(db), Clock).AcceptAsync(exceptionId, by, asHr, reason, CancellationToken.None);
    }
}
