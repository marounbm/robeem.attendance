using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Tests.Attendance;

/// <summary>The payroll eligibility gate (P11, study §18.3): only approved, exception-free days of an open pay period.</summary>
public sealed class PayrollAttendanceExtractTests : AttendanceEngineTestBase
{
    [Fact]
    public async Task ApprovedDayWithNoExceptions_IsReturned()
    {
        await WorkStandardDayAsync();

        var days = await ExtractAsync(Monday, Monday);

        var day = Assert.Single(days);
        Assert.Equal((EmployeeId, "E-1", Monday), (day.EmployeeId, day.EmployeeNumber, day.AttendanceDate));
        Assert.Equal((TimeSpan.FromHours(8), TimeSpan.Zero, TimeSpan.FromHours(1)), (day.TotalWorkingHours, day.TotalOvertimeHours, day.TotalBreakHours));
        var shift = Assert.Single(day.Shifts);
        Assert.Equal((Monday.At(8, 0), Monday.At(17, 0)), (shift.ActualCheckIn, shift.ActualCheckOut));
    }

    [Fact]
    public async Task ApprovedDay_WithAnExceptionStillOpenOrPendingForThatDate_IsExcluded_UntilItIsClosed()
    {
        await WorkStandardDayAsync();

        // The exception belongs to another (unscheduled) shift of the same date: the day is still not clean.
        var exceptionId = AddException(Monday, AttendanceExceptionStatus.Open);
        Assert.Empty(await ExtractAsync(Monday, Monday));

        SetExceptionStatus(exceptionId, AttendanceExceptionStatus.PendingCorrection);
        Assert.Empty(await ExtractAsync(Monday, Monday));

        SetExceptionStatus(exceptionId, AttendanceExceptionStatus.Closed);
        Assert.Single(await ExtractAsync(Monday, Monday));
    }

    [Fact]
    public async Task AnExceptionOnAnotherDate_DoesNotBlockTheDay()
    {
        await WorkStandardDayAsync();
        AddException(Monday.AddDays(1), AttendanceExceptionStatus.Open);

        Assert.Single(await ExtractAsync(Monday, Monday.AddDays(1)));
    }

    [Fact]
    public async Task OvertimeNeedingPreApproval_IsExcluded_UntilTheCorrectionExists()
    {
        AssignSchedule(StandardDay);
        AddDefaultPolicy(p => p.RequiresOvertimePreApproval = true);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(18, 30));
        await ProcessAsync(Monday);

        // The engine leaves it PendingReview, so it is out.
        Assert.Empty(await ExtractAsync(Monday, Monday));

        // A reviewer approves it (no review screen exists yet, so the test does what one would do). Still out: no pre-approval.
        ApproveRecordsOf(Monday);
        Assert.Empty(await ExtractAsync(Monday, Monday));

        AddCorrection(Monday, AttendanceCorrectionStatus.Pending);
        Assert.Empty(await ExtractAsync(Monday, Monday));

        AddCorrection(Monday, AttendanceCorrectionStatus.Applied);
        var day = Assert.Single(await ExtractAsync(Monday, Monday));
        Assert.Equal(TimeSpan.FromMinutes(90), day.TotalOvertimeHours);
    }

    [Fact]
    public async Task Overtime_WithoutAPreApprovalPolicy_NeedsNoCorrection()
    {
        AssignSchedule(StandardDay);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(18, 30));
        await ProcessAsync(Monday);

        var day = Assert.Single(await ExtractAsync(Monday, Monday));
        Assert.Equal(TimeSpan.FromMinutes(90), day.TotalOvertimeHours);
    }

    [Fact]
    public async Task DayInAClosedPayPeriod_IsExcluded_ButNotOneInAnOpenPeriod()
    {
        await WorkStandardDayAsync();
        AddPayPeriod(Monday.AddDays(-7), Monday.AddDays(-1), isClosed: true);
        AddPayPeriod(Monday, Monday.AddDays(6), isClosed: false);
        Assert.Single(await ExtractAsync(Monday, Monday.AddDays(6)));

        ClosePayPeriod(Monday);

        Assert.Empty(await ExtractAsync(Monday, Monday.AddDays(6)));
    }

    [Fact]
    public async Task OnlyTheDatesOfThePeriodAreReturned_BothEndsInclusive()
    {
        await WorkStandardDayAsync();

        Assert.Single(await ExtractAsync(Monday.AddDays(-3), Monday));
        Assert.Single(await ExtractAsync(Monday, Monday.AddDays(3)));
        Assert.Empty(await ExtractAsync(Monday.AddDays(1), Monday.AddDays(3)));
    }

    [Fact]
    public async Task TheCurrentVersionDecides_NotARetiredOne()
    {
        // Version 1 lacks the check-out and needs review: nothing to pay yet.
        AssignSchedule(StandardDay);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        await ProcessAsync(Monday);
        Assert.Empty(await ExtractAsync(Monday, Monday));

        // The check-out arrives: version 2 is current and approved, and version 1 stays history.
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 20));
        await ProcessAsync(Monday);

        var day = Assert.Single(await ExtractAsync(Monday, Monday));
        Assert.Equal(TimeSpan.FromMinutes(20), day.TotalOvertimeHours);
        Assert.Single(day.Shifts);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(2, db.AttendanceRecords.Count());
    }

    [Fact]
    public async Task APeriodThatEndsBeforeItStarts_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => ExtractAsync(Monday, Monday.AddDays(-1)));
    }

    [Fact]
    public async Task AnotherTenantsDays_AreNeverReturned()
    {
        await WorkStandardDayAsync();

        using var db = Database.CreateContext(TestDatabase.TenantB);
        var days = await new PayrollAttendanceExtract(db, new AttendancePolicyResolver(db)).GetApprovedRecordsAsync(Monday, Monday, CancellationToken.None);

        Assert.Empty(days);
    }

    private async Task WorkStandardDayAsync()
    {
        AssignSchedule(StandardDay);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));
        await ProcessAsync(Monday);
    }

    private async Task<IReadOnlyList<PayrollAttendanceDay>> ExtractAsync(DateTime from, DateTime to)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await new PayrollAttendanceExtract(db, new AttendancePolicyResolver(db)).GetApprovedRecordsAsync(from, to, CancellationToken.None);
    }

    private int AddException(DateTime date, AttendanceExceptionStatus status)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var exception = new AttendanceException
        {
            EmployeeId = EmployeeId,
            AttendanceDate = date,
            ExceptionType = AttendanceExceptionType.MissingPunch,
            MissingEventType = NormalizedEventType.CheckOut,
            Status = status,
        };
        db.AttendanceExceptions.Add(exception);
        db.SaveChanges();
        return exception.Id;
    }

    private void SetExceptionStatus(int exceptionId, AttendanceExceptionStatus status)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        db.AttendanceExceptions.Single(e => e.Id == exceptionId).Status = status;
        db.SaveChanges();
    }

    private void ApproveRecordsOf(DateTime date)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        foreach (var record in db.AttendanceRecords.Where(r => r.AttendanceDate == date))
        {
            record.Status = AttendanceRecordStatus.Approved;
        }

        foreach (var summary in db.AttendanceDaySummaries.Where(s => s.AttendanceDate == date))
        {
            summary.Status = AttendanceRecordStatus.Approved;
        }

        db.SaveChanges();
    }

    private void AddCorrection(DateTime date, AttendanceCorrectionStatus status)
    {
        var exceptionId = AddException(date, AttendanceExceptionStatus.Closed);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        db.AttendanceCorrections.Add(new AttendanceCorrection
        {
            AttendanceExceptionId = exceptionId,
            EmployeeId = EmployeeId,
            AttendanceDate = date,
            CorrectedEventType = NormalizedEventType.CheckOut,
            ProposedValue = date.At(18, 30),
            TimeZoneId = "UTC",
            Justification = "Stayed late on request.",
            RequestedByEmployeeId = EmployeeId,
            Status = status,
        });
        db.SaveChanges();
    }

    private void AddPayPeriod(DateTime start, DateTime end, bool isClosed)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        db.PayPeriods.Add(new PayPeriod { StartDate = start, EndDate = end, IsClosed = isClosed });
        db.SaveChanges();
    }

    private void ClosePayPeriod(DateTime startDate)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        db.PayPeriods.Single(p => p.StartDate == startDate).IsClosed = true;
        db.SaveChanges();
    }
}
