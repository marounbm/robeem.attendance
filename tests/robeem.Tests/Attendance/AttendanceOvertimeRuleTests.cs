using robeem.Application.Attendance;
using robeem.Application.Security;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// Whether overtime, and work with no scheduled shift, are exceptions is the rules' to decide: both are off to begin with, and overtime has a number of
/// minutes. They are in addition to the policy's pre-approval of overtime and to the review a day with no shift waits for. The shift is 08:00-17:00 on
/// Mondays only, so Tuesday is a day off.
/// </summary>
public sealed class AttendanceOvertimeRuleTests : AttendanceEngineTestBase
{
    private static readonly DateTime Tuesday = Monday.AddDays(1);

    public AttendanceOvertimeRuleTests()
    {
        AssignSchedule(new ShiftSpec("08:00", "17:00"));
        Clock.Local = Tuesday.At(20, 0);
    }

    [Fact]
    public async Task BothAreOffToBeginWith_OvertimeAndDayOffWorkRaiseNothing()
    {
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(18, 0));
        AddPunch(NormalizedEventType.CheckIn, Tuesday.At(9, 0));
        AddPunch(NormalizedEventType.CheckOut, Tuesday.At(12, 0));
        await ProcessAsync(Monday);
        await ProcessAsync(Tuesday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceExceptions);
        var settings = (await new AttendanceRuleService(db, CreateAudit(db), new SystemCurrentUser(), Clock).GetSettingsAsync(null, CancellationToken.None))!.Settings;
        Assert.False(settings.OvertimeRaises);
        Assert.False(settings.UnscheduledWorkRaises);
    }

    [Fact]
    public async Task WhenOn_OvertimeIsAnException_UnlessItIsWithinWhatTheRuleAllows()
    {
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(18, 0));
        await ConfigureAsync(0, s => (s.OvertimeRaises, s.OvertimeMinutesOver) = (true, 90));
        await ProcessAsync(Monday);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Empty(db.AttendanceExceptions);
        }

        // An hour of overtime is more than 30 minutes.
        await ConfigureAsync(1, s => s.OvertimeMinutesOver = 30);
        await ProcessAsync(Monday);
        using var check = Database.CreateContext(TestDatabase.TenantA);
        var exception = Assert.Single(check.AttendanceExceptions);
        Assert.Equal(AttendanceExceptionType.Overtime, exception.ExceptionType);
        Assert.Equal("Worked 60 minutes more than the scheduled time.", exception.Description);
    }

    [Fact]
    public async Task WhenOn_PunchesOnADayOff_AreAnException_ButPunchesOnAScheduledDayAreNot()
    {
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));
        AddPunch(NormalizedEventType.CheckIn, Tuesday.At(9, 0));
        AddPunch(NormalizedEventType.CheckOut, Tuesday.At(12, 0));
        await ConfigureAsync(0, s => s.UnscheduledWorkRaises = true);
        await ProcessAsync(Monday);
        await ProcessAsync(Tuesday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var exception = Assert.Single(db.AttendanceExceptions);
        Assert.Equal((AttendanceExceptionType.UnscheduledWork, Tuesday), (exception.ExceptionType, exception.AttendanceDate));
        Assert.Equal("Punches on 2026-10-06 with no shift scheduled.", exception.Description);
    }

    [Fact]
    public async Task HourlyStaff_NeverRaiseWorkWithNoScheduledShift()
    {
        using (var setup = Database.CreateContext(TestDatabase.TenantA))
        {
            setup.Profiles.Single(p => p.Id == EmployeeId).PayBasis = PayBasis.Hourly;
            setup.SaveChanges();
        }

        AddPunch(NormalizedEventType.CheckIn, Tuesday.At(9, 0));
        AddPunch(NormalizedEventType.CheckOut, Tuesday.At(12, 0));
        await ConfigureAsync(0, s => s.UnscheduledWorkRaises = true);
        await ProcessAsync(Tuesday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceExceptions);
    }

    [Fact]
    public async Task ASetPublishedBeforeTheFindingsExisted_IsReadWithBothRulesOff()
    {
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var set = new AttendanceRuleSet
            {
                Status = RuleSetStatus.Published,
                Version = 1,
                Title = "Rules version 1",
                ThresholdsInRules = true,
                ShortHoursInRules = true,
                AutoFixesInRules = true,
                BreakFixesInRules = true,
                LateEarlyInRules = true,
                OvertimeInRules = false,
            };
            db.AttendanceRuleSets.Add(set);
            db.AttendanceRules.Add(new AttendanceRule { AttendanceRuleSet = set, Priority = 10, Title = "No-show", IsEnabled = true, Stage = RuleStage.Exception, Action = RuleAction.RaiseException, Finding = RuleFinding.NoShow });
            db.SaveChanges();
        }

        AddPunch(NormalizedEventType.CheckIn, Tuesday.At(9, 0));
        AddPunch(NormalizedEventType.CheckOut, Tuesday.At(12, 0));
        await ProcessAsync(Tuesday);
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(check.AttendanceExceptions);

        var shown = (await new AttendanceRuleService(check, CreateAudit(check), new SystemCurrentUser(), Clock).GetCurrentAsync(null, CancellationToken.None))!;
        Assert.Contains(shown.Rules, r => r.Finding == nameof(RuleFinding.Overtime) && !r.IsEnabled);
        Assert.Contains(shown.Rules, r => r.Finding == nameof(RuleFinding.UnscheduledWork) && !r.IsEnabled);
    }

    private async Task ConfigureAsync(int expectedVersion, Action<RuleSettings> change)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var rules = new AttendanceRuleService(db, CreateAudit(db), new SystemCurrentUser(), Clock);
        var settings = (await rules.GetSettingsAsync(null, CancellationToken.None))!.Settings;
        change(settings);
        var saved = await rules.SaveSettingsAsync(new RuleSettingsSaveRequest { ExpectedVersion = expectedVersion, Settings = settings }, CancellationToken.None);
        Assert.True(saved.Succeeded, string.Join("; ", saved.Errors.Values));
    }
}
