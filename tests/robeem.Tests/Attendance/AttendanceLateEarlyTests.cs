using robeem.Application.Attendance;
using robeem.Application.Security;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// Whether a late check-in, or an early check-out, is an exception is the rules' to decide: both are off to begin with, and each has its own number of
/// minutes beyond the policy's grace. The shift is 08:00-17:00, no break, and the policy forgives nothing.
/// </summary>
public sealed class AttendanceLateEarlyTests : AttendanceEngineTestBase
{
    private readonly int _managerId;

    public AttendanceLateEarlyTests()
    {
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var manager = new Profile { EmployeeNumber = "M-1", FirstName = "Grace", LastName = "Hopper", Title = "Grace Hopper" };
            db.Add(manager);
            db.SaveChanges();
            db.Profiles.Single(p => p.Id == EmployeeId).ManagerId = manager.Id;
            db.SaveChanges();
            _managerId = manager.Id;
        }

        AssignSchedule(new ShiftSpec("08:00", "17:00"));
        Clock.Local = Monday.At(18, 0);
    }

    [Fact]
    public async Task BothAreOffToBeginWith_ALateDayRaisesNoLateCheckIn()
    {
        AddPunches("I@08:30,O@17:30");
        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceExceptions);
        var settings = (await new AttendanceRuleService(db, CreateAudit(db), new SystemCurrentUser(), Clock).GetSettingsAsync(null, CancellationToken.None))!.Settings;
        Assert.False(settings.LateCheckInRaises);
        Assert.False(settings.EarlyCheckOutRaises);
    }

    [Fact]
    public async Task WhenOn_ALateCheckIn_IsAnException_TiedToItsPunch_AndAcceptingItKeepsItAccepted()
    {
        AddPunches("I@08:30,O@17:30");
        await ConfigureAsync(0, s => s.LateCheckInRaises = true);
        await ProcessAsync(Monday);

        int id;
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var exception = Assert.Single(db.AttendanceExceptions);
            Assert.Equal((AttendanceExceptionType.LateCheckIn, AttendanceExceptionStatus.Open), (exception.ExceptionType, exception.Status));
            Assert.NotNull(exception.RelatedNormalizedTransactionId);
            Assert.Equal("Checked in 30 minutes late beyond the grace (scheduled 08:00).", exception.Description);
            id = exception.Id;
        }

        Assert.False((await AcceptAsync(id, EmployeeId, "I was stuck in traffic.")).Succeeded);
        var accepted = await AcceptAsync(id, _managerId, "Agreed, the road was closed.");
        Assert.True(accepted.Succeeded, string.Join("; ", accepted.Errors.Values));

        await ProcessAsync(Monday);
        using var check = Database.CreateContext(TestDatabase.TenantA);
        var settled = Assert.Single(check.AttendanceExceptions);
        Assert.Equal((AttendanceExceptionStatus.Closed, AttendanceExceptionResolutionMethod.Accepted), (settled.Status, settled.ResolutionMethod));
    }

    [Fact]
    public async Task WhenOn_AnEarlyCheckOut_IsAnException_AndItIsNotTheShortDaysRuleThatRaisesIt()
    {
        AddPunches("I@08:00,O@16:30");
        await ConfigureAsync(0, s =>
        {
            s.EarlyCheckOutRaises = true;
            s.ShortHoursRaises = false;
        });
        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var exception = Assert.Single(db.AttendanceExceptions);
        Assert.Equal(AttendanceExceptionType.EarlyCheckOut, exception.ExceptionType);
        Assert.NotNull(exception.RelatedNormalizedTransactionId);
        Assert.Equal("Checked out 30 minutes early beyond the grace (scheduled 17:00).", exception.Description);
    }

    [Fact]
    public async Task TheNumberOfMinutesIsTheRules_LessThanItIsNotRaised_MoreThanItIs()
    {
        AddPunches("I@08:10,O@17:10");
        await ConfigureAsync(0, s => (s.LateCheckInRaises, s.LateCheckInMinutesOver) = (true, 15));
        await ProcessAsync(Monday);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Empty(db.AttendanceExceptions);
        }

        await ConfigureAsync(1, s => s.LateCheckInMinutesOver = 5);
        await ProcessAsync(Monday);
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceExceptionType.LateCheckIn, Assert.Single(check.AttendanceExceptions).ExceptionType);
    }

    [Fact]
    public async Task ADayAnHourShort_IsForgiven_WithAToleranceOfAnHour_ButOneMinuteMoreIsNot()
    {
        // An hour short of the scheduled nine (the shift's 7-of-8 case): exactly what the rule allows.
        AddPunches("I@08:00,O@16:00");
        await ConfigureAsync(0, s => s.ShortHoursMinutesOver = 60);
        await ProcessAsync(Monday);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Empty(db.AttendanceExceptions);
            Assert.Equal(AttendanceRecordStatus.Approved, Assert.Single(db.AttendanceRecords).Status);
        }

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var checkOut = db.NormalizedAttendanceTransactions.Single(n => n.NormalizedEventType == NormalizedEventType.CheckOut);
            db.NormalizedAttendanceTransactions.Remove(checkOut);
            db.SaveChanges();
        }

        AddPunch(NormalizedEventType.CheckOut, Monday.At(15, 59));
        await ProcessAsync(Monday);
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceExceptionType.ShortHours, Assert.Single(check.AttendanceExceptions).ExceptionType);
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
                LateEarlyInRules = false,
            };
            db.AttendanceRuleSets.Add(set);
            db.AttendanceRules.Add(new AttendanceRule { AttendanceRuleSet = set, Priority = 10, Title = "No-show", IsEnabled = true, Stage = RuleStage.Exception, Action = RuleAction.RaiseException, Finding = RuleFinding.NoShow });
            db.SaveChanges();
        }

        AddPunches("I@08:30,O@17:30");
        await ProcessAsync(Monday);
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(check.AttendanceExceptions);

        var shown = (await new AttendanceRuleService(check, CreateAudit(check), new SystemCurrentUser(), Clock).GetCurrentAsync(null, CancellationToken.None))!;
        Assert.Contains(shown.Rules, r => r.Finding == nameof(RuleFinding.LateCheckIn) && !r.IsEnabled);
        Assert.Contains(shown.Rules, r => r.Finding == nameof(RuleFinding.EarlyCheckOut) && !r.IsEnabled);
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

    private async Task<robeem.Application.Common.SaveResult> AcceptAsync(int exceptionId, int by, string reason)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await new AttendanceExceptionService(db, CreateEngine(db), CreateAudit(db), Clock).AcceptAsync(exceptionId, by, false, reason, CancellationToken.None);
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
                _ => throw new ArgumentException($"Unknown punch type {parts[0]}."),
            };
            AddPunch(type, Monday.At(int.Parse(parts[1][..2]), int.Parse(parts[1][3..])));
        }
    }
}
