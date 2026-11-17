using robeem.Application.Attendance;
using robeem.Application.Security;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// A complete day worked for less time than the shift schedules raises an exception once the shift is over: late, left early, or any mix. A break that
/// ran over is the break rules' to judge. A manager or HR accepts it; the rules decide how much less is a problem. The shift is 08:00-17:00, no break.
/// </summary>
public sealed class AttendanceShortHoursTests : AttendanceEngineTestBase
{
    private readonly int _managerId;

    public AttendanceShortHoursTests()
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
    public async Task ADayWorkedShort_RaisesAnException_WithHowMuchWasMissed_AndTheDayWaitsForSomeoneToSettleIt()
    {
        AddPunches("I@08:30,O@15:00");

        var result = await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var exception = Assert.Single(db.AttendanceExceptions);
        Assert.Equal((AttendanceExceptionType.ShortHours, AttendanceExceptionStatus.Open, (int?)null, (NormalizedEventType?)null), (exception.ExceptionType, exception.Status, exception.RelatedNormalizedTransactionId, exception.MissingEventType));
        Assert.Equal("Worked 6:30 of the scheduled 9:00: 150 minutes short. Late by 30 minutes beyond the grace. Left 120 minutes early beyond the grace.", exception.Description);
        Assert.Equal(1, result.ExceptionsRaised);
        Assert.Equal(AttendanceRecordStatus.PendingReview, Assert.Single(db.AttendanceRecords).Status);
    }

    [Fact]
    public async Task ADayWorkedInFull_OrWithMoreTime_RaisesNothing()
    {
        AddPunches("I@08:00,O@17:00");
        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceExceptions);
        Assert.Equal(AttendanceRecordStatus.Approved, Assert.Single(db.AttendanceRecords).Status);
    }

    [Fact]
    public async Task WhatThePolicyForgives_AtTheStartAndTheEnd_IsNotShort_ButMoreThanThatIs()
    {
        AddDefaultPolicy(p => (p.LateGraceMinutes, p.EarlyDepartureGraceMinutes) = (10, 5));

        // 15 minutes short in all: exactly what is forgiven.
        AddPunches("I@08:10,O@16:55");
        await ProcessAsync(Monday);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Empty(db.AttendanceExceptions);
        }

        // One more minute is a day worked short.
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 11));
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var early = db.NormalizedAttendanceTransactions.Single(n => n.NormalizedEventType == NormalizedEventType.CheckIn && n.EventTimestampLocal == Monday.At(8, 10));
            db.NormalizedAttendanceTransactions.Remove(early);
            db.SaveChanges();
        }

        await ProcessAsync(Monday);
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceExceptionType.ShortHours, Assert.Single(check.AttendanceExceptions).ExceptionType);
    }

    [Fact]
    public async Task NothingIsRaisedWhileTheShiftIsStillRunning_BecauseTheEmployeeMayComeBack()
    {
        AddPunches("I@08:00,O@12:00");
        Clock.Local = Monday.At(13, 0);

        await ProcessAsync(Monday);

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Empty(db.AttendanceExceptions);
        }

        // Once the shift has ended, the day is short.
        Clock.Local = Monday.At(17, 1);
        await ProcessAsync(Monday);
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceExceptionType.ShortHours, Assert.Single(check.AttendanceExceptions).ExceptionType);
    }

    [Fact]
    public async Task ADayWithAPunchMissing_IsNotShort_ItsTimeWorkedIsNotKnown()
    {
        AddPunches("I@08:00");
        Clock.Local = Monday.At(20, 0);

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.DoesNotContain(db.AttendanceExceptions, e => e.ExceptionType == AttendanceExceptionType.ShortHours);
        Assert.Contains(db.AttendanceExceptions, e => e.ExceptionType == AttendanceExceptionType.MissingPunch);
    }

    [Fact]
    public async Task TimeOnApprovedLeave_IsNotMissedTime()
    {
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.EmployeeLeaves.Add(new EmployeeLeave
            {
                EmployeeId = EmployeeId,
                StartDate = Monday,
                EndDate = Monday,
                StartTime = "14:00",
                EndTime = "17:00",
                LeaveType = LeaveType.Other,
                IsPartDay = true,
            });
            db.SaveChanges();
        }

        AddPunches("I@08:00,O@14:00");
        await ProcessAsync(Monday);

        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(check.AttendanceExceptions);
    }

    [Fact]
    public async Task TheTimeABreakRanOver_IsTheBreakRulesToJudge_NotCountedAgainAsAShortDay()
    {
        using (var setup = Database.CreateContext(TestDatabase.TenantA))
        {
            var shift = setup.TimeScheduleShifts.Single();
            (shift.BreakTime, shift.BreakStartTime, shift.BreakEndTime) = (true, "12:00", "13:00");
            setup.SaveChanges();
        }

        // An hour and a half off for a scheduled hour: half an hour less worked, all of it the long break.
        AddPunches("I@08:00,BO@12:00,BI@13:30,O@17:00");
        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.DoesNotContain(db.AttendanceExceptions, e => e.ExceptionType == AttendanceExceptionType.ShortHours);
    }

    [Fact]
    public async Task AManagerAcceptsIt_TheDayIsApproved_AndItIsNotRaisedAgainWhenTheDayIsCalculatedAgain()
    {
        AddPunches("I@08:00,O@15:00");
        await ProcessAsync(Monday);
        var id = ExceptionId();

        var byEmployee = await AcceptAsync(id, EmployeeId, asHr: false, "I was ill.");
        var accepted = await AcceptAsync(id, _managerId, asHr: false, "Went home ill, agreed.");

        Assert.False(byEmployee.Succeeded);
        Assert.True(accepted.Succeeded, string.Join("; ", accepted.Errors.Values));
        await ProcessAsync(Monday);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var exception = Assert.Single(db.AttendanceExceptions);
        Assert.Equal((AttendanceExceptionStatus.Closed, AttendanceExceptionResolutionMethod.Accepted, "Went home ill, agreed."), (exception.Status, exception.ResolutionMethod, exception.ResolutionNote));
        Assert.Equal(AttendanceRecordStatus.Approved, Assert.Single(db.AttendanceRecords.Where(r => r.IsCurrent)).Status);
    }

    [Fact]
    public async Task TheRulesDecide_HowMuchIsShort_AndWhetherItIsRaisedAtAll()
    {
        AddPunches("I@08:00,O@16:00");
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var rules = new AttendanceRuleService(db, CreateAudit(db), new SystemCurrentUser(), Clock);
            var settings = (await rules.GetSettingsAsync(null, CancellationToken.None))!.Settings;
            Assert.True(settings.ShortHoursRaises);

            // An hour short is not a problem when up to two hours are allowed.
            settings.ShortHoursMinutesOver = 120;
            Assert.True((await rules.SaveSettingsAsync(new RuleSettingsSaveRequest { ExpectedVersion = 0, Settings = settings }, CancellationToken.None)).Succeeded);
        }

        await ProcessAsync(Monday);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Empty(db.AttendanceExceptions);

            // Switched off, it is never raised.
            var rules = new AttendanceRuleService(db, CreateAudit(db), new SystemCurrentUser(), Clock);
            var settings = (await rules.GetSettingsAsync(null, CancellationToken.None))!.Settings;
            settings.ShortHoursMinutesOver = 0;
            settings.ShortHoursRaises = false;
            Assert.True((await rules.SaveSettingsAsync(new RuleSettingsSaveRequest { ExpectedVersion = 1, Settings = settings }, CancellationToken.None)).Succeeded);
        }

        await ProcessAsync(Monday);
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(check.AttendanceExceptions);
    }

    [Fact]
    public async Task ASetPublishedBeforeTheFindingExisted_IsReadWithTheRuleOn_AndASetMadeSinceKeepsItsOwnChoice()
    {
        // A set from before: published with the other rules only.
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var set = new AttendanceRuleSet { Status = RuleSetStatus.Published, Version = 1, Title = "Rules version 1", ThresholdsInRules = true, ShortHoursInRules = false };
            db.AttendanceRuleSets.Add(set);
            db.AttendanceRules.Add(new AttendanceRule { AttendanceRuleSet = set, Priority = 10, Title = "No-show", IsEnabled = true, Stage = RuleStage.Exception, Action = RuleAction.RaiseException, Finding = RuleFinding.NoShow });
            db.SaveChanges();
        }

        AddPunches("I@08:00,O@15:00");
        await ProcessAsync(Monday);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Equal(AttendanceExceptionType.ShortHours, Assert.Single(db.AttendanceExceptions).ExceptionType);

            // The service shows the rule in force, so a change to it starts from the rule as it reads.
            var shown = (await new AttendanceRuleService(db, CreateAudit(db), new SystemCurrentUser(), Clock).GetCurrentAsync(null, CancellationToken.None))!;
            Assert.Contains(shown.Rules, r => r.Finding == nameof(RuleFinding.ShortHours) && r.IsEnabled);
        }

        // A set that says it has a say about the finding, and has no rule for it, does not raise it.
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var set = db.AttendanceRuleSets.Single();
            set.ShortHoursInRules = true;
            db.SaveChanges();
        }

        await ProcessAsync(Monday);
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceExceptionStatus.Closed, Assert.Single(check.AttendanceExceptions).Status);
    }

    private int ExceptionId()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return db.AttendanceExceptions.Single(e => e.ExceptionType == AttendanceExceptionType.ShortHours).Id;
    }

    private async Task<robeem.Application.Common.SaveResult> AcceptAsync(int exceptionId, int by, bool asHr, string reason)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await new AttendanceExceptionService(db, CreateEngine(db), CreateAudit(db), Clock).AcceptAsync(exceptionId, by, asHr, reason, CancellationToken.None);
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
