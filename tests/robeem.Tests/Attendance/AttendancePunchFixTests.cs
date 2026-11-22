using Microsoft.EntityFrameworkCore;
using robeem.Application.Attendance;
using robeem.Application.Security;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// The four automatic fixes of a day's punches, which a tenant switches on in the rules: a lone check-out near the start is the check-in, a lone
/// check-in near the end is the check-out, and of two close punches at the start or the end the first counts and the second is left out. They only
/// read punches that exist (marked inferred) and never touch a stored row (CLAUDE.md §1.5, §1.6). The shift is 08:00-17:00, no break.
/// </summary>
public sealed class AttendancePunchFixTests : AttendanceEngineTestBase
{
    public AttendancePunchFixTests()
    {
        AssignSchedule(new ShiftSpec("08:00", "17:00"));
        Clock.Local = Monday.At(18, 0);
    }

    [Fact]
    public async Task TheFixesAreOffUntilATenantSwitchesThemOn_SoTheDayRaisesItsExceptionsAsBefore()
    {
        AddPunches("O@08:00,O@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Contains(db.AttendanceExceptions, e => e.ExceptionType == AttendanceExceptionType.MissingPunch && e.MissingEventType == NormalizedEventType.CheckIn);
        Assert.Equal(AttendanceRecordStatus.PendingReview, Assert.Single(db.AttendanceRecords).Status);
    }

    [Fact]
    public async Task ALoneCheckOutNearTheStart_IsReadAsTheCheckIn_MarkedInferred_AndTheStoredPunchIsUntouched()
    {
        await EnableAsync(s => s.LoneCheckOutIsCheckIn = true);
        AddPunches("O@08:00,O@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((Monday.At(8, 0), AttendancePunchSource.Inferred, Monday.At(17, 0), AttendancePunchSource.Normalized), (record.ActualCheckIn, record.ActualCheckInSource, record.ActualCheckOut, record.ActualCheckOutSource));
        Assert.Equal((TimeSpan.FromHours(9), AttendanceRecordStatus.Approved), (record.WorkingHours, record.Status));
        Assert.Empty(db.AttendanceExceptions);
        Assert.Equal([NormalizedEventType.CheckOut, NormalizedEventType.CheckOut], db.NormalizedAttendanceTransactions.OrderBy(n => n.EventTimestampUtc).Select(n => n.NormalizedEventType));
    }

    [Fact]
    public async Task ALoneCheckOut_FarFromTheStart_IsLeftAlone()
    {
        await EnableAsync(s => s.LoneCheckOutIsCheckIn = true);
        AddPunches("O@10:30,O@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Contains(db.AttendanceExceptions, e => e.MissingEventType == NormalizedEventType.CheckIn);
    }

    [Fact]
    public async Task ALoneCheckOut_IsNotReadAsTheCheckIn_WhenTheDayHasACheckIn()
    {
        await EnableAsync(s => s.LoneCheckOutIsCheckIn = true);
        AddPunches("O@08:00,I@08:30,O@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Contains(db.AttendanceExceptions, e => e.ExceptionType == AttendanceExceptionType.InvalidSequence);
    }

    [Fact]
    public async Task ALoneCheckInNearTheEnd_IsReadAsTheCheckOut_MarkedInferred()
    {
        await EnableAsync(s => s.LoneCheckInIsCheckOut = true);
        AddPunches("I@08:00,I@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((Monday.At(17, 0), AttendancePunchSource.Inferred, AttendanceRecordStatus.Approved), (record.ActualCheckOut, record.ActualCheckOutSource, record.Status));
        Assert.Empty(db.AttendanceExceptions);
    }

    [Theory]
    [InlineData("I@08:00,I@08:07,O@17:00")]
    [InlineData("I@08:00,O@08:06,O@17:00")]
    public async Task TwoClosePunchesAtTheStart_CountAsOne_TheFirstIsTheCheckIn(string punches)
    {
        await EnableAsync(s => s.MorningDuplicateIsOne = true);
        AddPunches(punches);

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((Monday.At(8, 0), AttendancePunchSource.Normalized, Monday.At(17, 0), AttendanceRecordStatus.Approved), (record.ActualCheckIn, record.ActualCheckInSource, record.ActualCheckOut, record.Status));
        Assert.Empty(db.AttendanceExceptions);
    }

    [Fact]
    public async Task TwoPunchesAtTheStart_FurtherApartThanTheSetting_AreNotMerged_AndTheSettingIsTheTenants()
    {
        await EnableAsync(s => s.MorningDuplicateIsOne = true);
        AddPunches("I@08:00,I@08:11,O@17:00");

        await ProcessAsync(Monday);

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Contains(db.AttendanceExceptions, e => e.ExceptionType == AttendanceExceptionType.InvalidSequence);
        }

        // 11 minutes apart is close enough once the tenant says so, and the day is calculated again.
        await EnableAsync(s => s.MorningDuplicateGapMinutes = 15, expectedVersion: 1);
        await ProcessAsync(Monday);

        using var after = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceRecordStatus.Approved, after.AttendanceRecords.Single(r => r.IsCurrent).Status);
        Assert.DoesNotContain(after.AttendanceExceptions, e => e.Status == AttendanceExceptionStatus.Open);
    }

    [Theory]
    [InlineData("I@08:00,O@17:00,O@17:05", 17, 0)]
    [InlineData("I@08:00,I@17:00,O@17:05", 17, 0)]
    public async Task TwoClosePunchesAtTheEnd_CountAsOne_TheFirstIsTheCheckOut(string punches, int hour, int minute)
    {
        await EnableAsync(s => s.EveningDuplicateIsOne = true);
        AddPunches(punches);

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((Monday.At(hour, minute), AttendanceRecordStatus.Approved), (record.ActualCheckOut, record.Status));
        Assert.Empty(db.AttendanceExceptions);
    }

    [Fact]
    public async Task ACheckInThenACheckOutAtTheEnd_WithNoCheckInBefore_IsNotMerged()
    {
        await EnableAsync(s => s.EveningDuplicateIsOne = true);
        AddPunches("I@17:00,O@17:05");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((Monday.At(17, 0), Monday.At(17, 5)), (record.ActualCheckIn, record.ActualCheckOut));
    }

    [Fact]
    public async Task APunchAPersonAdded_IsNeverReadDifferentlyByAFix()
    {
        await EnableAsync(s => s.LoneCheckInIsCheckOut = true);
        AddPunches("I@08:00");
        AddAppliedSuppliedCheckIn(Monday.At(17, 0));

        await ProcessAsync(Monday);

        // The same last punch from the machine would have been read as the check-out; one a person added is left as it is.
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Null(Assert.Single(db.AttendanceRecords).ActualCheckOut);
        Assert.Contains(db.AttendanceExceptions, e => e.ExceptionType == AttendanceExceptionType.InvalidSequence);
    }

    [Fact]
    public async Task ASetPublishedBeforeTheFixesExisted_IsReadWithThemOff_AndTheSettingsShowThemOff()
    {
        // A tenant's earlier version: the same rules with the four fixes taken out and the set marked as made before they existed.
        await EnableAsync(s => s.GapWindowMinutes = 45);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var set = db.AttendanceRuleSets.Include(x => x.Rules).ThenInclude(r => r.Conditions).Single(x => x.Status == RuleSetStatus.Published);
            var fixes = set.Rules.Where(r => RuleSetSnapshot.IsCheckInOutFix(r.Action)).ToList();
            db.AttendanceRuleConditions.RemoveRange(fixes.SelectMany(r => r.Conditions));
            db.AttendanceRules.RemoveRange(fixes);
            set.AutoFixesInRules = false;
            db.SaveChanges();
        }

        using var read = Database.CreateContext(TestDatabase.TenantA);
        var shown = (await Service(read).GetSettingsAsync(null, CancellationToken.None))!;
        var snapshot = await new RuleSetProvider(read).ResolveAsync(new AttendancePolicy(), CancellationToken.None);

        Assert.True(shown.IsStandard);
        Assert.Equal((false, false, false, false, 120, 10), (shown.Settings.LoneCheckOutIsCheckIn, shown.Settings.LoneCheckInIsCheckOut, shown.Settings.MorningDuplicateIsOne, shown.Settings.EveningDuplicateIsOne, shown.Settings.LoneCheckOutWindowMinutes, shown.Settings.MorningDuplicateGapMinutes));
        Assert.False(snapshot.CanFixPunches);
    }

    [Theory]
    [InlineData("PunchGapMinutes", "LoneCheckOutIsCheckIn", false)]
    [InlineData("PunchGapMinutes", "KeepFirstMorningPunch", true)]
    [InlineData("PunchOffsetMinutes", "LoneCheckInIsCheckOut", true)]
    [InlineData("PunchOffsetMinutes", "RaiseException", false)]
    [InlineData("MinutesOver", "KeepFirstEveningPunch", false)]
    public void ThePunchFacts_AreOnlyForTheRulesThatFixPunches(string fact, string action, bool allowed)
    {
        var rule = new RuleDto
        {
            Name = "Rule",
            Stage = action == "RaiseException" ? "Exception" : "Interpretation",
            Action = action,
            Finding = action == "RaiseException" ? "NoShow" : null,
            Conditions = [new ConditionDto { Fact = fact, Operator = "LessOrEqual", Value = 10 }],
        };

        Assert.Equal(allowed, RuleValidator.Validate([rule]).Count == 0);
    }

    [Fact]
    public void TheFixesReadAsSentences()
    {
        var sentence = RuleSentence.Describe(new RuleDto
        {
            Action = "KeepFirstMorningPunch",
            Conditions = [new ConditionDto { Fact = "PunchOffsetMinutes", Operator = "LessOrEqual", Value = 120 }, new ConditionDto { Fact = "PunchGapMinutes", Operator = "LessOrEqual", Value = 10 }],
        });

        Assert.Equal("When the punch is at most 120 minutes from the scheduled start or end of the shift and the two punches are at most 10 minutes apart, count the first punch as the check-in and leave the second out.", sentence);
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

    /// <summary>Stores a check-in that a person's request added and an approver applied.</summary>
    private void AddAppliedSuppliedCheckIn(DateTime at)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        db.AttendanceCorrections.Add(new AttendanceCorrection
        {
            AttendanceApproval = new AttendanceApproval
            {
                Type = AttendanceApprovalType.Correction,
                EmployeeId = EmployeeId,
                AttendanceDate = at.Date,
                RequestedByEmployeeId = EmployeeId,
                RequestedAt = at,
                Status = AttendanceApprovalStatus.Applied,
                AppliedAt = at,
            },
            EmployeeId = EmployeeId,
            AttendanceDate = at.Date,
            Kind = AttendanceCorrectionKind.SupplyPunch,
            CorrectedEventType = NormalizedEventType.CheckIn,
            ProposedValue = at,
            TimeZoneId = db.NormalizedAttendanceTransactions.First().TimeZoneId,
            Justification = "Forgot",
            RequestedByEmployeeId = EmployeeId,
            IsPending = false,
        });
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
