using Microsoft.EntityFrameworkCore;
using robeem.Application.Attendance;
using robeem.Application.Security;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// The two automatic fixes of a break punch of the wrong type, which a tenant switches on in the rules: a break-in where no break is open and the punch is
/// near the scheduled start of the break is the break-out; a break-out where a break is already open and the punch is near the scheduled end of the break is
/// the break-in. They only read punches that exist (marked inferred) and never touch a stored row (CLAUDE.md §1.5, §1.6). The shift is 08:00-17:00 with a break
/// from 12:00 to 13:00.
/// </summary>
public sealed class AttendanceBreakPunchFixTests : AttendanceEngineTestBase
{
    public AttendanceBreakPunchFixTests()
    {
        AssignSchedule(new ShiftSpec("08:00", "17:00", "12:00", "13:00"));
        Clock.Local = Monday.At(18, 0);
    }

    [Fact]
    public async Task TheFixesAreOffUntilATenantSwitchesThemOn_SoABreakInSaidTwiceRaisesItsException()
    {
        AddPunches("I@08:00,BI@12:05,BI@13:00,O@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.NotEmpty(db.AttendanceExceptions);
        Assert.Null(db.AttendanceRecords.Single().ActualBreakOut);
    }

    [Theory]
    [InlineData("I@08:00,BI@12:05,BI@13:00,O@17:00")]
    [InlineData("I@08:00,BI@12:05,O@17:00")]
    public async Task ABreakInNearTheStartOfTheBreak_WhereNoBreakIsOpen_IsReadAsTheBreakOut_MarkedInferred(string punches)
    {
        await EnableAsync(s => s.BreakInIsBreakOut = true);
        AddPunches(punches);

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((Monday.At(12, 5), AttendancePunchSource.Inferred), (record.ActualBreakOut, record.ActualBreakOutSource));
        Assert.DoesNotContain(db.AttendanceExceptions, e => e.Status == AttendanceExceptionStatus.Open && e.ExceptionType == AttendanceExceptionType.InvalidSequence);

        // The stored punch is exactly what the machine sent.
        Assert.Equal(NormalizedEventType.BreakIn, db.NormalizedAttendanceTransactions.Single(n => n.EventTimestampLocal == Monday.At(12, 5)).NormalizedEventType);
    }

    [Fact]
    public async Task TwiceBreakIn_IsAFullBreak_WhenTheFirstIsReadAsTheBreakOut()
    {
        await EnableAsync(s => s.BreakInIsBreakOut = true);
        AddPunches("I@08:00,BI@12:05,BI@13:00,O@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((Monday.At(13, 0), AttendancePunchSource.Normalized, TimeSpan.FromMinutes(55), AttendanceRecordStatus.Approved), (record.ActualBreakIn, record.ActualBreakInSource, record.BreakDuration, record.Status));
        Assert.Empty(db.AttendanceExceptions);
    }

    [Fact]
    public async Task ABreakIn_FarFromTheStartOfTheBreak_IsLeftAlone()
    {
        await EnableAsync(s => s.BreakInIsBreakOut = true);
        AddPunches("I@08:00,BI@10:00,BI@13:00,O@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.NotEmpty(db.AttendanceExceptions);
        Assert.Null(db.AttendanceRecords.Single().ActualBreakOut);
    }

    [Fact]
    public async Task ABreakIn_NearTheEndOfTheBreak_WhereABreakIsOpen_IsNotTouched()
    {
        await EnableAsync(s => s.BreakInIsBreakOut = true);
        AddPunches("I@08:00,BO@12:00,BI@12:55,O@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((AttendancePunchSource.Normalized, AttendancePunchSource.Normalized), (record.ActualBreakOutSource, record.ActualBreakInSource));
    }

    [Fact]
    public async Task ABreakOutNearTheEndOfTheBreak_WhereABreakIsOpen_IsReadAsTheBreakIn_MarkedInferred()
    {
        await EnableAsync(s => s.BreakOutIsBreakIn = true);
        AddPunches("I@08:00,BO@12:00,BO@12:55,O@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((Monday.At(12, 0), AttendancePunchSource.Normalized, Monday.At(12, 55), AttendancePunchSource.Inferred), (record.ActualBreakOut, record.ActualBreakOutSource, record.ActualBreakIn, record.ActualBreakInSource));
        Assert.Equal((TimeSpan.FromMinutes(55), AttendanceRecordStatus.Approved), (record.BreakDuration, record.Status));
        Assert.Empty(db.AttendanceExceptions);
        Assert.Equal(NormalizedEventType.BreakOut, db.NormalizedAttendanceTransactions.Single(n => n.EventTimestampLocal == Monday.At(12, 55)).NormalizedEventType);
    }

    [Fact]
    public async Task ABreakOut_FarFromTheEndOfTheBreak_IsLeftAlone()
    {
        await EnableAsync(s => s.BreakOutIsBreakIn = true);
        AddPunches("I@08:00,BO@12:00,BO@15:00,O@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Contains(db.AttendanceExceptions, e => e.ExceptionType == AttendanceExceptionType.InvalidSequence);
    }

    [Fact]
    public async Task TheWindowIsTheTenants_ABreakInTwentyMinutesLateIsReadOnlyWhenTheWindowIsWideEnough()
    {
        await EnableAsync(s => s.BreakInIsBreakOut = true);
        AddPunches("I@08:00,BI@12:40,BI@13:00,O@17:00");

        await ProcessAsync(Monday);

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.NotEmpty(db.AttendanceExceptions);
            Assert.Null(db.AttendanceRecords.Single().ActualBreakOut);
        }

        await EnableAsync(s => s.BreakInIsBreakOutWindowMinutes = 45, expectedVersion: 1);
        await ProcessAsync(Monday);

        using var after = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(Monday.At(12, 40), after.AttendanceRecords.Single(r => r.IsCurrent).ActualBreakOut);
    }

    [Fact]
    public async Task ASetPublishedBeforeTheBreakFixesExisted_IsReadWithThemOff_AndTheSettingsShowThemOff()
    {
        await EnableAsync(s => s.GapWindowMinutes = 45);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var set = db.AttendanceRuleSets.Include(x => x.Rules).ThenInclude(r => r.Conditions).Single(x => x.Status == RuleSetStatus.Published);
            var fixes = set.Rules.Where(r => RuleSetSnapshot.IsBreakFix(r.Action)).ToList();
            db.AttendanceRuleConditions.RemoveRange(fixes.SelectMany(r => r.Conditions));
            db.AttendanceRules.RemoveRange(fixes);
            set.BreakFixesInRules = false;
            db.SaveChanges();
        }

        using var read = Database.CreateContext(TestDatabase.TenantA);
        var shown = (await Service(read).GetSettingsAsync(null, CancellationToken.None))!;
        var snapshot = await new RuleSetProvider(read).ResolveAsync(new AttendancePolicy(), CancellationToken.None);

        Assert.True(shown.IsStandard);
        Assert.Equal((false, 30, false, 30), (shown.Settings.BreakInIsBreakOut, shown.Settings.BreakInIsBreakOutWindowMinutes, shown.Settings.BreakOutIsBreakIn, shown.Settings.BreakOutIsBreakInWindowMinutes));
        Assert.False(snapshot.CanFixPunches);
    }

    [Theory]
    [InlineData("PunchOffsetMinutes", "BreakInIsBreakOut", true)]
    [InlineData("PunchOffsetMinutes", "BreakOutIsBreakIn", true)]
    [InlineData("PunchGapMinutes", "BreakInIsBreakOut", false)]
    [InlineData("GapMinutes", "BreakOutIsBreakIn", false)]
    public void TheBreakFixes_UseThePunchOffsetFactOnly(string fact, string action, bool allowed)
    {
        var rule = new RuleDto
        {
            Name = "Rule",
            Stage = "Interpretation",
            Action = action,
            Conditions = [new ConditionDto { Fact = fact, Operator = "LessOrEqual", Value = 30 }],
        };

        Assert.Equal(allowed, RuleValidator.Validate([rule]).Count == 0);
    }

    [Fact]
    public void TheBreakFixesReadAsSentences_AboutTheBreakNotTheShift()
    {
        var condition = new ConditionDto { Fact = "PunchOffsetMinutes", Operator = "LessOrEqual", Value = 30 };

        Assert.Equal(
            "When the punch is at most 30 minutes from the scheduled start of the break, read the break-in as the break-out (marked as inferred).",
            RuleSentence.Describe(new RuleDto { Action = "BreakInIsBreakOut", Conditions = [condition] }));
        Assert.Equal(
            "When the punch is at most 30 minutes from the scheduled end of the break, read the break-out as the break-in (marked as inferred).",
            RuleSentence.Describe(new RuleDto { Action = "BreakOutIsBreakIn", Conditions = [condition] }));
    }

    private AttendanceRuleService Service(RobeemDbContext db) => new(db, CreateAudit(db), new SystemCurrentUser(), Clock);

    /// <summary>Changes the standard settings of the tenant-wide rules and publishes them, as HR does on the Rules page.</summary>
    private async Task EnableAsync(Action<RuleSettings> change, int expectedVersion = 0)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var service = Service(db);
        var settings = (await service.GetSettingsAsync(null, CancellationToken.None))!.Settings with { };
        change(settings);
        var saved = await service.SaveSettingsAsync(new RuleSettingsSaveRequest { ExpectedVersion = expectedVersion, Settings = settings }, CancellationToken.None);
        Assert.True(saved.Succeeded, string.Join("; ", saved.Errors.Select(e => $"{e.Key}: {e.Value}")));
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
