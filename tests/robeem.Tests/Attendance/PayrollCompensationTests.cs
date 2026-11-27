using Microsoft.Extensions.Logging.Abstractions;
using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Application.HR.Absence;
using robeem.Application.HR.Leaves;
using robeem.Application.Security;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// The compensation report: overtime and under time are lines payroll decides (they are not exceptions), penalties accumulate, a leave decision
/// adds leaves to the employee's leaves, and a period closes only when its days are ready. The shift is 08:00-17:00 with a 12:00-13:00 break,
/// on Mondays; the pay period is the calendar month unless a test gives a cycle.
/// </summary>
public sealed class PayrollCompensationTests : AttendanceEngineTestBase
{
    private static readonly DateTime October = new(2026, 10, 1);
    private static readonly DateTime Tuesday = Monday.AddDays(1);

    private readonly int _payrollAdminId;

    public PayrollCompensationTests()
    {
        // Not an active employee of the lists: only the one who decides.
        _payrollAdminId = AddPerson("P-1", "Pat", "Payroll", active: false);
        AssignSchedule(StandardDay);

        // A day worked short is an exception only where a tenant's rules say so; payroll decides it here.
        ConfigureAsync(s => s.ShortHoursRaises = false).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task ADayWithOvertime_IsAnOvertimeLine_ThatCountsByDefault_AndCanBeIgnored()
    {
        await WorkAsync("I@08:00,BO@12:00,BI@13:00,O@18:00");

        var report = (await ReportAsync())!;

        var line = Assert.Single(report.Lines);
        Assert.Equal((CompensationLine.Overtime, 60, "Weekday", true, null), (line.LineKind, line.Minutes, line.OvertimeKind, line.NeedsDecision, line.Decision));
        Assert.Equal((October, new DateTime(2026, 10, 31), true, false), (report.PeriodStart, report.PeriodEnd, report.PeriodHasEnded, report.IsClosed));
        Assert.Equal((1, 540 - 60, 60, 60, 60, 60, 1), (report.Summary.PaidDays, report.Summary.RegularMinutes, report.Summary.OvertimeMinutes, report.Summary.OvertimeCountedMinutes, report.Summary.BalanceMinutes, report.Summary.OvertimeLineMinutes, report.Summary.UndecidedLines));

        var ignored = await DecideAsync(Line(Monday, CompensationLine.Overtime, "Ignore"));

        Assert.True(ignored.Succeeded, string.Join("; ", ignored.Errors.Values));
        var after = (await ReportAsync())!;
        var decided = Assert.Single(after.Lines);
        Assert.Equal(("Ignore", false, "Pat Payroll"), (decided.Decision, decided.NeedsDecision, decided.DecidedByName));
        Assert.Equal((60, 60, 0, 0, 0, 0), (after.Summary.OvertimeMinutes, after.Summary.OvertimeIgnoredMinutes, after.Summary.OvertimeCountedMinutes, after.Summary.BalanceMinutes, after.Summary.OvertimeLineMinutes, after.Summary.UndecidedLines));

        await DecideAsync(Line(Monday, CompensationLine.Overtime, "Overtime"));

        var accepted = (await ReportAsync())!.Summary;
        Assert.Equal((60, 60, 60), (accepted.OvertimeAcceptedMinutes, accepted.OvertimeCountedMinutes, accepted.OvertimeLineMinutes));
    }

    [Fact]
    public async Task OvertimeIsNotAnException_TheDayIsApprovedAndNothingWaitsInTheQueues()
    {
        await WorkAsync("I@08:00,BO@12:00,BI@13:00,O@18:00");

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceExceptions);
        Assert.Equal(AttendanceRecordStatus.Approved, Assert.Single(db.AttendanceDaySummaries).Status);
        Assert.Empty(db.AttendanceApprovals);
    }

    [Fact]
    public async Task ADecisionOnADayThatWasCalculatedAgainWithOtherMinutes_NeedsANewDecision()
    {
        await WorkAsync("I@08:00,BO@12:00,BI@13:00,O@18:00");
        await DecideAsync(Line(Monday, CompensationLine.Overtime, "Ignore"));

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var checkOut = db.NormalizedAttendanceTransactions.Single(n => n.NormalizedEventType == NormalizedEventType.CheckOut);
            checkOut.EventTimestampLocal = Monday.At(18, 30);
            checkOut.EventTimestampUtc = new DateTimeOffset(Monday.At(18, 30), TimeSpan.Zero);
            db.SaveChanges();
        }

        await ProcessAsync(Monday);

        var line = Assert.Single((await ReportAsync())!.Lines);
        Assert.Equal((90, null, true), (line.Minutes, line.Decision, line.NeedsDecision));
        Assert.Equal(90, (await ReportAsync())!.Summary.OvertimeCountedMinutes);
    }

    [Fact]
    public async Task UnderTime_StaysAsUnderTimeUntilDecided_AndLateMinutesAreAlreadyPenalised()
    {
        await AddPolicyAsync(p => (p.LateGraceMinutes, p.PayLatePenaltyPercent, p.PayUndertimePenaltyPercent) = (10, 100, 200));
        await WorkAsync("I@08:30,BO@12:00,BI@13:00,O@17:00");

        var report = (await ReportAsync())!;

        // Half an hour short, of which 20 minutes (after the 10 minutes' grace) are late and are penalised as such.
        var line = Assert.Single(report.Lines);
        Assert.Equal((CompensationLine.Undertime, 30, 20, 20, true), (line.LineKind, line.Minutes, line.LateMinutes, line.PenaltyMinutes, line.NeedsDecision));
        Assert.Equal(["Ignore", "Penalise", "PaidLeave", "UnpaidLeave"], line.AllowedDecisions);
        Assert.Equal((30, 30, -30, 0, 30, 20), (report.Summary.UndertimeMinutes, report.Summary.UndertimeRemainingMinutes, report.Summary.BalanceMinutes, report.Summary.OvertimeLineMinutes, report.Summary.DeficitMinutes, report.Summary.PenaltyMinutes));
    }

    [Fact]
    public async Task PenalisingUnderTime_AddsOnlyTheMinutesThatAreNotAlreadyLateOrEarly_AndTakesThemOutOfTheBalance()
    {
        await AddPolicyAsync(p => (p.LateGraceMinutes, p.PayLatePenaltyPercent, p.PayUndertimePenaltyPercent) = (10, 100, 200));
        await WorkAsync("I@08:30,BO@12:00,BI@13:00,O@17:00");

        var decided = await DecideAsync(Line(Monday, CompensationLine.Undertime, "Penalise"));

        Assert.True(decided.Succeeded, string.Join("; ", decided.Errors.Values));
        var report = (await ReportAsync())!;
        var line = Assert.Single(report.Lines);

        // 20 late minutes at 100%, plus the 10 minutes of the 30 that are neither late nor early, at 200%.
        Assert.Equal((40, 40, "Penalise"), (line.PenaltyMinutes, line.RunningPenaltyMinutes, line.Decision));
        Assert.Equal((30, 0, 0, 40), (report.Summary.UndertimePenalisedMinutes, report.Summary.UndertimeRemainingMinutes, report.Summary.BalanceMinutes, report.Summary.PenaltyMinutes));
    }

    [Fact]
    public async Task IgnoringUnderTime_RemovesItFromTheBalance_WithoutAPenaltyForIt()
    {
        await AddPolicyAsync(p => (p.LateGraceMinutes, p.PayUndertimePenaltyPercent) = (10, 200));
        await WorkAsync("I@08:30,BO@12:00,BI@13:00,O@17:00");

        await DecideAsync(Line(Monday, CompensationLine.Undertime, "Ignore"));

        var summary = (await ReportAsync())!.Summary;
        Assert.Equal((30, 0, 0, 0), (summary.UndertimeIgnoredMinutes, summary.UndertimeRemainingMinutes, summary.BalanceMinutes, summary.PenaltyMinutes));
    }

    [Fact]
    public async Task ADayWithBothKindsOfMinutes_OffsetsTheOvertimeWithTheUnderTime_AndEndsWithOneOvertimeLine()
    {
        // Monday: 90 minutes of overtime. Next Monday: 30 minutes short.
        await WorkAsync("I@08:00,BO@12:00,BI@13:00,O@18:30");
        await WorkAsync("I@08:30,BO@12:00,BI@13:00,O@17:00", Monday.AddDays(7));

        var summary = (await ReportAsync())!.Summary;

        Assert.Equal((90, 30, 90, 30, 60, 60, 0), (summary.OvertimeMinutes, summary.UndertimeMinutes, summary.OvertimeCountedMinutes, summary.UndertimeRemainingMinutes, summary.BalanceMinutes, summary.OvertimeLineMinutes, summary.DeficitMinutes));
    }

    [Fact]
    public async Task APaidLeaveDecision_AddsALeaveToTheEmployeesLeaves_AndTheDayIsSettledAgain()
    {
        await AddPolicyAsync(p => p.LateGraceMinutes = 10);
        await WorkAsync("I@08:30,BO@12:00,BI@13:00,O@17:00");

        var decided = await DecideAsync(Line(Monday, CompensationLine.Undertime, "PaidLeave", "Sick"));

        Assert.True(decided.Succeeded, string.Join("; ", decided.Errors.Values));
        using var db = Database.CreateContext(TestDatabase.TenantA);

        // The missed half hour, from the start of the shift to the check-in, as a part-day leave of the type payroll chose.
        var leave = Assert.Single(db.EmployeeLeaves);
        Assert.Equal((EmployeeId, LeaveType.Sick, true, Monday, "08:00", "08:30"), (leave.EmployeeId, leave.LeaveType, leave.IsPartDay, leave.StartDate, leave.StartTime, leave.EndTime));
        Assert.Contains("compensation report", leave.Notes);
        var decision = Assert.Single(db.PayCompensationDecisions);
        Assert.Equal((CompensationLineKind.Undertime, CompensationDecisionKind.PaidLeave, 30, leave.Id.ToString(), _payrollAdminId), (decision.LineKind, decision.Decision, decision.Minutes, decision.LeaveIds, decision.DecidedById));
        Assert.Contains(db.AttendanceAuditLogs.ToList(), a => a.Action == AttendanceAuditActions.CompensationDecided && a.Details!.Contains($"leave {leave.Id} added"));

        // Saving the leave settled the day again: the leave excuses the time, so there is no under time left, and the report says it was covered.
        Assert.Equal(TimeSpan.Zero, db.AttendanceDaySummaries.Single().TotalUndertimeHours);
        var report = (await ReportAsync())!;
        var covered = Assert.Single(report.Lines);
        Assert.Equal((true, "PaidLeave", 30, false), (covered.IsCoveredByLeave, covered.Decision, covered.Minutes, covered.NeedsDecision));
        Assert.Equal((30, 0, 0), (report.Summary.UndertimePaidLeaveMinutes, report.Summary.UndertimeRemainingMinutes, report.Summary.BalanceMinutes));
    }

    [Fact]
    public async Task AnUnpaidLeaveDecision_AddsUnpaidLeave_AndTheReportKeepsItApart()
    {
        await AddPolicyAsync(p => p.LateGraceMinutes = 10);
        await WorkAsync("I@08:30,BO@12:00,BI@13:00,O@16:30");

        var decided = await DecideAsync(Line(Monday, CompensationLine.Undertime, "UnpaidLeave"));

        Assert.True(decided.Succeeded, string.Join("; ", decided.Errors.Values));
        using var db = Database.CreateContext(TestDatabase.TenantA);

        // Late half an hour at the start, early half an hour at the end: two leaves.
        var leaves = db.EmployeeLeaves.OrderBy(l => l.StartTime).ToList();
        Assert.Equal([("08:00", "08:30"), ("16:30", "17:00")], leaves.Select(l => (l.StartTime, l.EndTime)));
        Assert.All(leaves, l => Assert.Equal(LeaveType.Unpaid, l.LeaveType));
        var summary = (await ReportAsync())!.Summary;
        Assert.Equal((60, 0, 0), (summary.UndertimeUnpaidLeaveMinutes, summary.UndertimeRemainingMinutes, summary.BalanceMinutes));
    }

    [Fact]
    public async Task ALeaveThatWouldOverlapOneTheEmployeeHas_IsRefused_AndNothingIsSaved()
    {
        await AddPolicyAsync(p => p.LateGraceMinutes = 10);
        await WorkAsync("I@08:30,BO@12:00,BI@13:00,O@17:00");
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.EmployeeLeaves.Add(new EmployeeLeave { EmployeeId = EmployeeId, LeaveType = LeaveType.Annual, StartDate = Monday, EndDate = Monday, IsPartDay = true, StartTime = "08:10", EndTime = "08:20" });
            db.SaveChanges();
        }

        var decided = await DecideAsync(Line(Monday, CompensationLine.Undertime, "PaidLeave"));

        Assert.Contains("overlaps another leave", decided.Errors.Values.Single());
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Single(check.EmployeeLeaves);
        Assert.Empty(check.PayCompensationDecisions);
        Assert.Empty(check.AttendanceAuditLogs.Where(a => a.Action == AttendanceAuditActions.CompensationDecided));
    }

    [Fact]
    public async Task ADecisionTheLineDoesNotAllow_AnUnknownLine_OrABulkLeave_IsRefused_AndNothingIsSaved()
    {
        await AddPolicyAsync(p => p.LateGraceMinutes = 10);
        await WorkAsync("I@08:30,BO@12:00,BI@13:00,O@17:00");

        var overtimeOnUnderTime = await DecideAsync(Line(Monday, CompensationLine.Undertime, "Overtime"));
        var noSuchLine = await DecideAsync(Line(Monday.AddDays(7), CompensationLine.Undertime, "Ignore"));
        var noSuchKind = await DecideAsync(Line(Monday, CompensationLine.Overtime, "Ignore"));
        var notADecision = await DecideAsync(Line(Monday, CompensationLine.Undertime, "Forgive"));
        var twice = await DecideAsync(Line(Monday, CompensationLine.Undertime, "Ignore"), Line(Monday, CompensationLine.Undertime, "Penalise"));
        var bulkLeave = await DecideAsync(bulk: "PaidLeave");
        var nothing = await DecideAsync();
        var badLeaveType = await DecideAsync(Line(Monday, CompensationLine.Undertime, "PaidLeave", "Unpaid"));

        Assert.Contains("Overtime is not allowed", overtimeOnUnderTime.Errors.Values.Single());
        Assert.Contains("no Undertime line", noSuchLine.Errors.Values.Single());
        Assert.Contains("no Overtime line", noSuchKind.Errors.Values.Single());
        Assert.Contains("not a decision", notADecision.Errors.Values.Single());
        Assert.Contains("decided twice", twice.Errors.Values.Single());
        Assert.Contains("Ignore or Penalise", bulkLeave.Errors.Values.Single());
        Assert.Contains("at least one line", nothing.Errors.Values.Single());
        Assert.Contains("Paid leave can be", badLeaveType.Errors.Values.Single());
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.PayCompensationDecisions);
        Assert.Empty(db.EmployeeLeaves);
    }

    [Fact]
    public async Task AnOvertimeLine_CannotBeMadeLeave()
    {
        await WorkAsync("I@08:00,BO@12:00,BI@13:00,O@18:00");

        var decided = await DecideAsync(Line(Monday, CompensationLine.Overtime, "PaidLeave"));

        Assert.Contains("PaidLeave is not allowed", decided.Errors.Values.Single());
        Assert.Equal(["Ignore", "Overtime"], Assert.Single((await ReportAsync())!.Lines).AllowedDecisions);
    }

    [Fact]
    public async Task AllTheUnderTimeWithNoDecision_CanBeDecidedAtOnce()
    {
        await AddPolicyAsync(p => (p.LateGraceMinutes, p.PayUndertimePenaltyPercent) = (10, 100));
        await WorkAsync("I@08:30,BO@12:00,BI@13:00,O@17:00");
        await WorkAsync("I@08:45,BO@12:00,BI@13:00,O@17:00", Monday.AddDays(7));
        await DecideAsync(Line(Monday, CompensationLine.Undertime, "Ignore"));

        var decided = await DecideAsync(bulk: "Penalise");

        Assert.True(decided.Succeeded);
        Assert.Equal(1, decided.Id);
        var lines = (await ReportAsync())!.Lines;
        Assert.Equal(["Ignore", "Penalise"], lines.Select(l => l.Decision));
    }

    [Fact]
    public async Task WorkOnADayOff_IsAnOvertimeLine_WhenTheSchedulesRuleSaysSo()
    {
        await WorkAsync("I@09:00,O@13:00", Tuesday);

        var line = Assert.Single((await ReportAsync())!.Lines);

        Assert.Equal((CompensationLine.Overtime, "DayOff", 240, "Day off"), (line.LineKind, line.OvertimeKind, line.Minutes, line.Scheduled));
    }

    [Fact]
    public async Task WorkOnADayOff_IsOrdinaryHours_WhenTheScheduleDoesNotCountItAsOvertime()
    {
        await AddPolicyAsync(p => p.PayDayOffWorkIsOvertime = false);
        await WorkAsync("I@09:00,O@13:00", Tuesday);

        var report = (await ReportAsync())!;

        Assert.Empty(report.Lines);
        Assert.Equal((240, 240, 0), (report.Summary.WorkedMinutes, report.Summary.RegularMinutes, report.Summary.OvertimeMinutes));
    }

    [Fact]
    public async Task WorkOnAPublicHoliday_IsAHolidayOvertimeLine_WithAllTheWorkInIt()
    {
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.PublicHolidays.Add(new PublicHoliday { Date = Monday, Title = "Holiday" });
            db.SaveChanges();
        }

        await WorkAsync("I@08:00,BO@12:00,BI@13:00,O@17:00", approveAll: true);

        var line = Assert.Single((await ReportAsync())!.Lines);

        Assert.Equal((CompensationLine.Overtime, "Holiday", 480, "Public holiday"), (line.LineKind, line.OvertimeKind, line.Minutes, line.Scheduled));
    }

    [Fact]
    public async Task AnEmployeePaidByTheHour_HasHoursOnly_NoLinesAndNoPenalties()
    {
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.Profiles.Single(p => p.Id == EmployeeId).PayBasis = PayBasis.Hourly;
            db.SaveChanges();
        }

        await AddPolicyAsync(p => (p.PayLatePenaltyPercent, p.PayEarlyPenaltyPercent) = (100, 100));
        await WorkAsync("I@08:30,O@19:30");

        var report = (await ReportAsync())!;

        Assert.Empty(report.Lines);
        Assert.Equal((1, 660, 660, 0), (report.Summary.PaidDays, report.Summary.WorkedMinutes, report.Summary.RegularMinutes, report.Summary.PenaltyMinutes));
    }

    [Fact]
    public async Task APeriodWithADayThatIsNotReady_CannotBeClosed_UntilTheDayIsSettled_AndThenItsDecisionsAreLocked()
    {
        await WorkAsync("I@08:00,BO@12:00,BI@13:00,O@18:00");
        var exceptionId = AddOpenException(Monday.AddDays(1));

        var blocked = (await ReportAsync())!;

        Assert.Equal([Monday.AddDays(1)], blocked.NotReadyDays);
        Assert.False(blocked.CanClose);
        Assert.Contains("not ready for payroll (2026-10-06)", blocked.CannotCloseReason);
        var refused = await CloseAsync();
        Assert.Contains("not ready for payroll", refused.Errors.Values.Single());
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Empty(db.PayPeriods);
        }

        SettleException(exceptionId);
        Assert.True((await ReportAsync())!.CanClose);
        Assert.True((await CloseAsync()).Succeeded);

        // Closed: payroll reads none of the days, but the report still shows them, and the decisions are locked.
        var closed = (await ReportAsync())!;
        var line = Assert.Single(closed.Lines);
        Assert.Equal((true, 0, false), (closed.IsClosed, line.AllowedDecisions.Count, line.NeedsDecision));
        Assert.Contains("closed", (await DecideAsync(Line(Monday, CompensationLine.Overtime, "Ignore"))).Errors.Values.Single());
        Assert.Contains("already closed", (await CloseAsync()).Errors.Values.Single());
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(await new PayrollAttendanceExtract(check, new AttendancePolicyResolver(check)).GetApprovedRecordsAsync(October, new DateTime(2026, 10, 31), (string?)null, CancellationToken.None));

        // Reopening needs a reason; then payroll reads the days again and the lines can be decided.
        Assert.False((await ReopenAsync(string.Empty)).Succeeded);
        Assert.True((await ReopenAsync("A late fix.")).Succeeded);
        Assert.True((await DecideAsync(Line(Monday, CompensationLine.Overtime, "Ignore"))).Succeeded);
    }

    [Fact]
    public async Task APeriodThatHasNotEnded_CannotBeClosed()
    {
        await WorkAsync("I@08:00,BO@12:00,BI@13:00,O@17:00");
        Clock.Local = new DateTime(2026, 10, 20, 10, 0, 0);

        var report = (await Service(Database.CreateContext(TestDatabase.TenantA)).GetReportAsync(EmployeeId, IPayrollCompensationService.ContainingPeriod, Monday, CancellationToken.None))!;
        var refused = await CloseAsync();

        Assert.Equal((false, false), (report.PeriodHasEnded, report.CanClose));
        Assert.Contains("ends on 2026-10-31", report.CannotCloseReason);
        Assert.Contains("ends on 2026-10-31", refused.Errors.Values.Single());
    }

    [Fact]
    public async Task EachEmployeeHasTheirOwnPeriod_FromTheirOwnCycle_AndClosingOneLeavesTheOtherPayable()
    {
        var other = AddPerson("E-2", "Grace", "Hopper");
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.PayCycles.Add(new PayCycle { EmployeeId = EmployeeId, Frequency = PayCycleFrequency.Weekly, AnchorDate = Monday });
            db.SaveChanges();
        }

        await WorkAsync("I@08:00,BO@12:00,BI@13:00,O@18:00");

        using var db2 = Database.CreateContext(TestDatabase.TenantA);
        var page = await Service(db2).ListAsync("E-", IPayrollCompensationService.ContainingPeriod, Monday.AddDays(3), 1, 20, CancellationToken.None);

        // Ada is paid weekly from Monday 5th; Grace has no cycle, so the calendar month.
        var ada = page.Items.Single(i => i.EmployeeId == EmployeeId);
        var grace = page.Items.Single(i => i.EmployeeId == other);
        Assert.Equal((Monday, Monday.AddDays(6), 60), (ada.PeriodStart, ada.PeriodEnd, ada.Summary.OvertimeMinutes));
        Assert.Equal((October, new DateTime(2026, 10, 31), 0), (grace.PeriodStart, grace.PeriodEnd, grace.Summary.PaidDays));
    }

    [Fact]
    public async Task TheLatestMode_FindsThePeriodOfTheLatestDayWithAttendance_WhateverTodayIs_AndAnEmployeeWithNoneFallsBackToTheLastFinished()
    {
        var quiet = AddPerson("E-2", "Grace", "Hopper");
        await WorkAsync("I@08:00,BO@12:00,BI@13:00,O@18:00");

        // Long after the attendance: the last finished period is December's, which holds nothing; the latest with attendance is October's.
        Clock.Local = new DateTime(2027, 1, 20, 10, 0, 0);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var service = Service(db);
        var last = await service.ListAsync("E-", IPayrollCompensationService.LastPeriod, null, 1, 20, CancellationToken.None);
        var latest = await service.ListAsync("E-", IPayrollCompensationService.LatestPeriod, null, 1, 20, CancellationToken.None);

        Assert.Equal(0, last.Items.Single(i => i.EmployeeId == EmployeeId).Summary.PaidDays);
        var ada = latest.Items.Single(i => i.EmployeeId == EmployeeId);
        Assert.Equal((October, new DateTime(2026, 10, 31), 1, 60), (ada.PeriodStart, ada.PeriodEnd, ada.Summary.PaidDays, ada.Summary.OvertimeMinutes));
        var none = latest.Items.Single(i => i.EmployeeId == quiet);
        Assert.Equal((new DateTime(2026, 12, 1), new DateTime(2026, 12, 31)), (none.PeriodStart, none.PeriodEnd));
    }

    [Fact]
    public async Task TheOverviewIsPagedByEmployeeNumber_FollowsTheSearch_AndAPageOutOfRangeIsTheLast()
    {
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            for (var i = 2; i <= 25; i++)
            {
                db.Profiles.Add(new Profile { EmployeeNumber = $"E-{i}", FirstName = $"First{i}", LastName = "Person", Title = $"First{i} Person" });
            }

            db.SaveChanges();
        }

        using var check = Database.CreateContext(TestDatabase.TenantA);
        var service = Service(check);
        var pages = new List<CompensationOverviewItem>();
        foreach (var page in new[] { 1, 2, 3 })
        {
            pages.AddRange((await service.ListAsync(null, null, null, page, 10, CancellationToken.None)).Items);
        }

        var past = await service.ListAsync(null, null, null, 99, 10, CancellationToken.None);
        var all = await service.ListAllAsync(null, null, null, CancellationToken.None);
        var searched = await service.ListAsync("first2", null, null, 1, 50, CancellationToken.None);

        // Every active employee once (the base employee, E-1, and 24 more), shorter numbers first so that E-2 comes before E-10.
        Assert.Equal(25, pages.Count);
        Assert.Equal(pages.Select(p => p.EmployeeNumber), all.Select(p => p.EmployeeNumber));
        Assert.Equal(Enumerable.Range(1, 25).Select(i => $"E-{i}").OrderBy(n => n.Length).ThenBy(n => n, StringComparer.Ordinal), pages.Select(p => p.EmployeeNumber));
        Assert.Equal((3, 5, 25), (past.Page, past.Items.Count, past.Total));
        Assert.Equal(["E-2", "E-20", "E-21", "E-22", "E-23", "E-24", "E-25"], searched.Items.Select(i => i.EmployeeNumber).OrderBy(n => n.Length).ThenBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task APeriodNeedsTheEmployeesOwnFirstDay_AndAnUnknownEmployeeIsMissing()
    {
        await WorkAsync("I@08:00,BO@12:00,BI@13:00,O@18:00");

        var wrongStart = await DecideAsync(periodStart: October.AddDays(3), lines: [Line(Monday, CompensationLine.Overtime, "Ignore")]);
        var wrongClose = await Service(Database.CreateContext(TestDatabase.TenantA)).ClosePeriodAsync(new CompensationCloseRequest { EmployeeId = EmployeeId, PeriodStart = October.AddDays(3) }, CancellationToken.None);
        var unknown = await Service(Database.CreateContext(TestDatabase.TenantA)).DecideAsync(new CompensationDecisionRequest { EmployeeId = 9999, PeriodStart = October }, _payrollAdminId, CancellationToken.None);

        Assert.Contains("not the first day", wrongStart.Errors.Values.Single());
        Assert.Contains("not the first day", wrongClose.Errors.Values.Single());
        Assert.True(unknown.NotFound);
        Assert.Null(await Service(Database.CreateContext(TestDatabase.TenantA)).GetReportAsync(9999, null, null, CancellationToken.None));
    }

    private static CompensationLineDecision Line(DateTime date, string kind, string decision, string? leaveType = null) =>
        new() { Date = date, LineKind = kind, Decision = decision, LeaveType = leaveType };

    private async Task ConfigureAsync(Action<RuleSettings> change)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var rules = new AttendanceRuleService(db, CreateAudit(db), new SystemCurrentUser(), Clock);
        var settings = (await rules.GetSettingsAsync(null, CancellationToken.None))!.Settings;
        change(settings);
        var saved = await rules.SaveSettingsAsync(new RuleSettingsSaveRequest { ExpectedVersion = 0, Settings = settings }, CancellationToken.None);
        Assert.True(saved.Succeeded, string.Join("; ", saved.Errors.Values));
    }

    private Task AddPolicyAsync(Action<AttendancePolicy> configure)
    {
        AddDefaultPolicy(configure);
        return Task.CompletedTask;
    }

    private int AddPerson(string number, string first, string last, bool active = true)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var person = new Profile { EmployeeNumber = number, FirstName = first, LastName = last, Title = $"{first} {last}", IsActive = active };
        db.Add(person);
        db.SaveChanges();
        return person.Id;
    }

    /// <summary>Punches the day (Monday unless given), calculates it when it is over, and settles a day nobody reviews. The clock then stands well after October.</summary>
    private async Task WorkAsync(string punches, DateTime? day = null, bool approveAll = false)
    {
        var date = day ?? Monday;
        foreach (var punch in punches.Split(','))
        {
            var parts = punch.Split('@');
            var type = parts[0] switch
            {
                "I" => NormalizedEventType.CheckIn,
                "O" => NormalizedEventType.CheckOut,
                "BO" => NormalizedEventType.BreakOut,
                _ => NormalizedEventType.BreakIn,
            };
            AddPunch(type, date.At(int.Parse(parts[1][..2]), int.Parse(parts[1][3..])));
        }

        Clock.Local = date.At(23, 0);
        await ProcessAsync(date);

        // Work with no shift waits for a review of its own; payroll's gate is what is under test here, so approve it as a reviewer would.
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            foreach (var record in db.AttendanceRecords.Where(r => r.AttendanceDate == date && (approveAll || r.TimeScheduleShiftId == null) && r.Status != AttendanceRecordStatus.Approved))
            {
                record.Status = AttendanceRecordStatus.Approved;
            }

            foreach (var summary in db.AttendanceDaySummaries.Where(s => s.AttendanceDate == date && s.Status == AttendanceRecordStatus.PendingReview))
            {
                summary.Status = AttendanceRecordStatus.Approved;
            }

            db.SaveChanges();
        }

        Clock.Local = new DateTime(2026, 11, 5, 10, 0, 0);
    }

    private async Task<CompensationReport?> ReportAsync()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await Service(db).GetReportAsync(EmployeeId, IPayrollCompensationService.LastPeriod, null, CancellationToken.None);
    }

    private Task<SaveResult> DecideAsync(params CompensationLineDecision[] lines) => DecideAsync(null, October, lines);

    private Task<SaveResult> DecideAsync(string? bulk) => DecideAsync(bulk, October, []);

    private async Task<SaveResult> DecideAsync(string? bulk, DateTime periodStart, IReadOnlyList<CompensationLineDecision> lines)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await Service(db).DecideAsync(
            new CompensationDecisionRequest { EmployeeId = EmployeeId, PeriodStart = periodStart, Lines = lines, UndecidedUndertime = bulk },
            _payrollAdminId,
            CancellationToken.None);
    }

    private Task<SaveResult> DecideAsync(DateTime periodStart, IReadOnlyList<CompensationLineDecision> lines) => DecideAsync(null, periodStart, lines);

    private async Task<SaveResult> CloseAsync()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await Service(db).ClosePeriodAsync(new CompensationCloseRequest { EmployeeId = EmployeeId, PeriodStart = October }, CancellationToken.None);
    }

    private async Task<SaveResult> ReopenAsync(string reason)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await Service(db).ReopenPeriodAsync(new CompensationReopenRequest { EmployeeId = EmployeeId, PeriodStart = October, Reason = reason }, CancellationToken.None);
    }

    private int AddOpenException(DateTime date)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var exception = new AttendanceException
        {
            EmployeeId = EmployeeId,
            AttendanceDate = date,
            ExceptionType = AttendanceExceptionType.MissingPunch,
            MissingEventType = NormalizedEventType.CheckOut,
            Status = AttendanceExceptionStatus.Open,
        };
        db.AttendanceExceptions.Add(exception);
        db.SaveChanges();
        return exception.Id;
    }

    private void SettleException(int id)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        db.AttendanceExceptions.Single(e => e.Id == id).Status = AttendanceExceptionStatus.Closed;
        db.SaveChanges();
    }

    private AttendanceReprocessingService Reprocessing(RobeemDbContext db) =>
        new(db, CreateEngine(db), CreateAudit(db), Clock, NullLogger<AttendanceReprocessingService>.Instance);

    /// <summary>The leave service as the host builds it: saving a leave settles the days it covers.</summary>
    private LeaveService Leaves(RobeemDbContext db) =>
        new(db, new AttendanceAbsenceChangeNotifier(Reprocessing(db), NullLogger<AttendanceAbsenceChangeNotifier>.Instance));

    private PayrollCompensationService Service(RobeemDbContext db)
    {
        var policies = new AttendancePolicyResolver(db);
        return new PayrollCompensationService(
            db,
            new PayrollAttendanceExtract(db, policies),
            policies,
            new HrAbsenceCalendar(db),
            Leaves(db),
            new PayPeriodService(db, CreateAudit(db, new TestUser(_payrollAdminId))),
            CreateAudit(db, new TestUser(_payrollAdminId)),
            Clock);
    }
}
