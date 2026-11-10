using Microsoft.Extensions.Logging.Abstractions;
using robeem.Application.Attendance;
using robeem.Application.HR.Absence;
using robeem.Application.Security;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// The rules HR can see and change: the policy's settings written as rules, the evaluator, the validator, the draft-and-publish life of a
/// rule set, and what the engine does under a published set. The shift is 08:00-17:00 with a break from 12:00 to 13:00, on Mondays only.
/// </summary>
public sealed class AttendanceRulesTests : AttendanceEngineTestBase
{
    [Fact]
    public void ThePolicysSettings_AreWrittenAsRules_AndDecideWhatIsRaised()
    {
        var policy = new AttendancePolicy { RaiseExceptionOnLongBreak = true, RaiseExceptionOnNoBreak = false };

        var rules = RuleSetSnapshot.FromPolicy(policy);

        Assert.Equal((0, "the policy"), (rules.Version, rules.Source));
        Assert.True(rules.Raises(RuleFinding.LongBreak, new RuleFacts()));
        Assert.False(rules.Raises(RuleFinding.LateBreak, new RuleFacts()));
        Assert.False(rules.Raises(RuleFinding.EarlyBreak, new RuleFacts()));
        Assert.False(rules.Raises(RuleFinding.NoBreak, new RuleFacts()));
        Assert.True(rules.Raises(RuleFinding.NoShow, new RuleFacts()));
        Assert.False(rules.CanInferBreak);

        var inferring = RuleSetSnapshot.FromPolicy(new AttendancePolicy { BreakInferenceMode = BreakInferenceMode.FromGap, InferredBreakWindowMinutes = 30, InferredBreakMaxMinutes = 60 });
        Assert.True(inferring.CanInferBreak);
        Assert.True(inferring.InfersBreak(new RuleFacts { ShiftHasBreak = true, GapMinutes = 60, GapStartOffsetMinutes = 30, GapExcusedPercent = 50 }));
        Assert.False(inferring.InfersBreak(new RuleFacts { ShiftHasBreak = true, GapMinutes = 61, GapStartOffsetMinutes = 30 }));
        Assert.False(inferring.InfersBreak(new RuleFacts { ShiftHasBreak = true, GapMinutes = 60, GapStartOffsetMinutes = 31 }));
        Assert.False(inferring.InfersBreak(new RuleFacts { ShiftHasBreak = true, GapMinutes = 60, GapStartOffsetMinutes = 0, GapExcusedPercent = 51 }));
        Assert.False(inferring.InfersBreak(new RuleFacts { ShiftHasBreak = false, GapMinutes = 10 }));
    }

    [Fact]
    public void ForAFinding_TheFirstRuleWhoseConditionsHoldDecides_ADisabledRuleIsSkipped_AndNoRuleMeansNotRaised()
    {
        RuleSnapshot Rule(int priority, RuleAction action, bool enabled = true, params ConditionSnapshot[] conditions) =>
            new($"r{priority}", priority, enabled, RuleStage.Exception, action, RuleFinding.NoShow, action == RuleAction.SuppressException ? "Because" : null, conditions);

        var overnight = new ConditionSnapshot(RuleFact.ShiftIsOvernight, RuleOperator.Equals, 1);
        var suppressFirst = new RuleSetSnapshot(1, "published version 1", [Rule(20, RuleAction.RaiseException), Rule(10, RuleAction.SuppressException, true, overnight)]);
        var raiseFirst = new RuleSetSnapshot(2, "published version 2", [Rule(10, RuleAction.RaiseException), Rule(20, RuleAction.SuppressException)]);
        var disabled = new RuleSetSnapshot(3, "published version 3", [Rule(10, RuleAction.SuppressException, false), Rule(20, RuleAction.RaiseException)]);

        Assert.False(suppressFirst.Raises(RuleFinding.NoShow, new RuleFacts { ShiftIsOvernight = true }));
        Assert.True(suppressFirst.Raises(RuleFinding.NoShow, new RuleFacts { ShiftIsOvernight = false }));
        Assert.True(raiseFirst.Raises(RuleFinding.NoShow, new RuleFacts()));
        Assert.True(disabled.Raises(RuleFinding.NoShow, new RuleFacts()));
        Assert.False(disabled.Raises(RuleFinding.LongBreak, new RuleFacts()));
        Assert.False(new RuleSetSnapshot(4, "published version 4", []).Raises(RuleFinding.NoShow, new RuleFacts()));
    }

    [Theory]
    [InlineData("Weekend", "Exception", "RaiseException", "NoShow", null, "Rules[0].Conditions[0].Fact")]
    [InlineData("GapMinutes", "Exception", "RaiseException", "NoShow", null, "Rules[0].Conditions[0].Fact")]
    [InlineData("MinutesOver", "Interpretation", "InferBreak", null, null, "Rules[0].Conditions[0].Fact")]
    [InlineData("MinutesOver", "Interpretation", "RaiseException", "NoShow", null, "Rules[0].Stage")]
    [InlineData("MinutesOver", "Exception", "InferBreak", null, null, "Rules[0].Stage")]
    [InlineData("MinutesOver", "Exception", "RaiseException", null, null, "Rules[0].Finding")]
    [InlineData("MinutesOver", "Exception", "RaiseException", "Hunger", null, "Rules[0].Finding")]
    [InlineData("MinutesOver", "Exception", "SuppressException", "NoShow", " ", "Rules[0].Reason")]
    [InlineData("MinutesOver", "Exception", "Fly", "NoShow", null, "Rules[0].Action")]
    public void ARuleThatDoesNotMakeSense_IsRefused_WithTheFieldNamed(string fact, string stage, string action, string? finding, string? reason, string expectedKey)
    {
        var rule = new RuleDto { Name = "Rule", Stage = stage, Action = action, Finding = finding, Reason = reason, Conditions = [new ConditionDto { Fact = fact, Operator = "GreaterThan", Value = 5 }] };

        var errors = RuleValidator.Validate([rule]);

        Assert.Contains(expectedKey, errors.Keys);
    }

    [Fact]
    public void TheConditionsAreHeldToTheirFactsRanges_AndAContradictionIsRefused()
    {
        RuleDto Rule(params ConditionDto[] conditions) =>
            new() { Name = "Rule", Stage = "Exception", Action = "RaiseException", Finding = "LongBreak", Conditions = [.. conditions] };
        ConditionDto Condition(string fact, string op, int value) => new() { Fact = fact, Operator = op, Value = value };

        Assert.Empty(RuleValidator.Validate([Rule(Condition("MinutesOver", "GreaterOrEqual", 30), Condition("MinutesOver", "LessThan", 90), Condition("ShiftHasBreak", "Equals", 1))]));
        Assert.Empty(RuleValidator.Validate([new RuleDto { Name = "Always", Stage = "Exception", Action = "RaiseException", Finding = "NoShow" }]));

        Assert.Contains("Rules[0].Conditions[0].Value", RuleValidator.Validate([Rule(Condition("ShiftHasBreak", "Equals", 5))]).Keys);
        Assert.Contains("Rules[0].Conditions[0].Value", RuleValidator.Validate([Rule(Condition("ShiftHasBreak", "GreaterThan", 0))]).Keys);
        Assert.Contains("Rules[0].Conditions[0].Value", RuleValidator.Validate([Rule(Condition("MinutesOver", "Equals", -1))]).Keys);
        Assert.Contains("Rules[0].Conditions[0].Value", RuleValidator.Validate([Rule(Condition("MinutesOver", "Equals", 1441))]).Keys);
        Assert.Contains("Rules[0].Conditions", RuleValidator.Validate([Rule(Condition("MinutesOver", "GreaterThan", 60), Condition("MinutesOver", "LessThan", 30))]).Keys);
        Assert.Contains("Rules[0].Conditions", RuleValidator.Validate([Rule(Condition("MinutesOver", "Equals", 30), Condition("MinutesOver", "NotEquals", 30))]).Keys);
        Assert.Contains("Rules[0].Name", RuleValidator.Validate([new RuleDto { Name = " ", Stage = "Exception", Action = "RaiseException", Finding = "NoShow" }]).Keys);
        Assert.Contains("Rules", RuleValidator.Validate(Enumerable.Range(0, 51).Select(_ => Rule()).ToList()).Keys);
    }

    [Fact]
    public void ARuleReadsAsASentence()
    {
        var current = RuleSetSnapshot.FromPolicy(new AttendancePolicy { BreakInferenceMode = BreakInferenceMode.FromGap });
        var infer = RuleSentence.Describe(new RuleDto
        {
            Action = "InferBreak",
            Conditions = [new ConditionDto { Fact = "ShiftHasBreak", Operator = "Equals", Value = 1 }, new ConditionDto { Fact = "GapMinutes", Operator = "LessOrEqual", Value = 120 }],
        });
        var suppress = RuleSentence.Describe(new RuleDto { Action = "SuppressException", Finding = "NoShow", Reason = "Unannounced days are fine." });
        var always = RuleSentence.Describe(new RuleDto { Action = "RaiseException", Finding = "LongBreak", Conditions = [new ConditionDto { Fact = "ShiftIsOvernight", Operator = "NotEquals", Value = 1 }] });

        Assert.Equal("When the shift has a break and the gap lasts at most 120 minutes, read the gap as the break (marked as inferred).", infer);
        Assert.Equal("Always do not raise an exception for a no-show (reason: Unannounced days are fine.).", suppress);
        Assert.Equal("When the shift ends the same day, raise an exception for a long break.", always);
        Assert.Equal(6, current.Rules.Count);
    }

    [Fact]
    public async Task ARuleSet_IsADraftFirst_ThenPublishedAsTheNextVersion_AndThePreviousOneIsRetired()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var service = Service(db);

        var current = (await service.GetCurrentAsync(null, CancellationToken.None))!;
        Assert.Equal(("Policy", 0, 6, null), (current.Status, current.Version, current.Rules.Count, current.Id));
        Assert.Contains("read the gap as the break", current.Rules[0].Sentence);
        Assert.Equal(["The whole company (the default policy)"], (await service.ListScopesAsync(CancellationToken.None)).Select(s => s.Title));

        var draftId = (await service.CreateDraftAsync(null, CancellationToken.None)).Id!.Value;
        var second = await service.CreateDraftAsync(null, CancellationToken.None);
        Assert.Contains("draft already", second.Errors["id"]);
        Assert.Equal(draftId, (await service.GetDraftAsync(null, CancellationToken.None))!.Id);

        // A bad draft is not saved; the draft keeps its rules.
        var draft = (await service.GetAsync(draftId, CancellationToken.None))!;
        draft.Rules[1].Conditions.Add(new ConditionDto { Fact = "Weekend", Operator = "Equals", Value = 1 });
        var refused = await service.SaveDraftAsync(draftId, new RulesSaveRequest { Rules = draft.Rules }, CancellationToken.None);
        Assert.Contains("Rules[1].Conditions[0].Fact", refused.Errors.Keys);
        Assert.Empty((await service.GetAsync(draftId, CancellationToken.None))!.Rules[1].Conditions);

        draft.Rules[1].Conditions[0] = new ConditionDto { Fact = "MinutesOver", Operator = "GreaterOrEqual", Value = 30 };
        draft.Rules[1].IsEnabled = true;
        Assert.True((await service.SaveDraftAsync(draftId, new RulesSaveRequest { Rules = draft.Rules, Notes = "Long breaks over half an hour." }, CancellationToken.None)).Succeeded);
        Assert.Equal(("Draft", 0), ((await service.GetAsync(draftId, CancellationToken.None))!.Status, (await service.GetAsync(draftId, CancellationToken.None))!.Version));

        Assert.True((await service.PublishAsync(draftId, null, CancellationToken.None)).Succeeded);

        var published = (await service.GetCurrentAsync(null, CancellationToken.None))!;
        Assert.Equal(("Published", 1, draftId), (published.Status, published.Version, published.Id!.Value));
        Assert.Equal("Long breaks over half an hour.", published.Notes);
        Assert.Contains("the finding is at least 30 minutes over", published.Rules[1].Sentence);
        Assert.Null(await service.GetDraftAsync(null, CancellationToken.None));

        // A published set is never edited; a change is a new draft, published as the next version.
        Assert.Contains("never edited", (await service.SaveDraftAsync(draftId, new RulesSaveRequest { Rules = draft.Rules }, CancellationToken.None)).Errors["id"]);
        Assert.Contains("Only a draft", (await service.DiscardDraftAsync(draftId, CancellationToken.None)).Errors["id"]);

        var next = (await service.CreateDraftAsync(null, CancellationToken.None)).Id!.Value;
        Assert.Contains("the finding is at least 30 minutes over", (await service.GetAsync(next, CancellationToken.None))!.Rules[1].Sentence);
        await service.PublishAsync(next, "Again", CancellationToken.None);

        var history = await service.ListHistoryAsync(null, CancellationToken.None);
        Assert.Equal([(2, "Published"), (1, "Retired")], history.Select(h => (h.Version, h.Status)));
        Assert.Contains(db.AttendanceAuditLogs, a => a.Action == AttendanceAuditActions.RuleSetPublished && a.Details!.Contains("version 2 published"));
    }

    [Fact]
    public async Task ADraftCanBeThrownAway_AScopeMustHaveAPolicy_AndASuppressionIsAuditedWithItsReason()
    {
        int scheduleWithPolicy, scheduleWithout;
        using (var setup = Database.CreateContext(TestDatabase.TenantA))
        {
            var withPolicy = new TimeSchedule { Title = "Night shift", CyclePeriod = 7, FirstCycleDay = DayOfWeek.Monday };
            var without = new TimeSchedule { Title = "Day shift", CyclePeriod = 7, FirstCycleDay = DayOfWeek.Monday };
            setup.AddRange(withPolicy, without);
            setup.SaveChanges();
            setup.AttendancePolicies.Add(new AttendancePolicy { Title = "Night", TimeScheduleId = withPolicy.Id });
            setup.SaveChanges();
            (scheduleWithPolicy, scheduleWithout) = (withPolicy.Id, without.Id);
        }

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var service = Service(db);

        Assert.Contains(nameof(RuleScopeDto.TimeScheduleId), (await service.CreateDraftAsync(scheduleWithout, CancellationToken.None)).Errors.Keys);
        Assert.Null(await service.GetCurrentAsync(scheduleWithout, CancellationToken.None));
        Assert.Equal(["The whole company (the default policy)", "Night shift"], (await service.ListScopesAsync(CancellationToken.None)).Select(s => s.Title));

        var draftId = (await service.CreateDraftAsync(scheduleWithPolicy, CancellationToken.None)).Id!.Value;
        Assert.True((await service.DiscardDraftAsync(draftId, CancellationToken.None)).Succeeded);
        Assert.Null(await service.GetDraftAsync(scheduleWithPolicy, CancellationToken.None));
        Assert.True((await service.DiscardDraftAsync(draftId, CancellationToken.None)).NotFound);

        var again = (await service.CreateDraftAsync(scheduleWithPolicy, CancellationToken.None)).Id!.Value;
        var draft = (await service.GetAsync(again, CancellationToken.None))!;
        draft.Rules.Insert(0, new RuleDto { Name = "Unannounced days are fine", Stage = "Exception", Action = "SuppressException", Finding = "NoShow", Reason = "Casual staff may skip a day." });
        Assert.True((await service.SaveDraftAsync(again, new RulesSaveRequest { Rules = draft.Rules }, CancellationToken.None)).Succeeded);
        Assert.True((await service.PublishAsync(again, null, CancellationToken.None)).Succeeded);

        Assert.Contains(db.AttendanceAuditLogs, a => a.Action == AttendanceAuditActions.RuleSetPublished && a.Details!.Contains("Suppresses: NoShow: Casual staff may skip a day."));
    }

    [Fact]
    public async Task ASuppressRule_StopsANoShowBeingRaised_AndTheDayIsStillNotApprovable()
    {
        AssignSchedule(StandardDay);
        await PublishAsync(rules => rules.Insert(0, new RuleDto { Name = "Away without notice is fine", Stage = "Exception", Action = "SuppressException", Finding = "NoShow", Reason = "Casual staff." }));
        Clock.Local = Monday.At(20, 0);

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceExceptions);
        Assert.Equal(AttendanceRecordStatus.PendingReview, Assert.Single(db.AttendanceRecords).Status);
    }

    [Fact]
    public async Task ALongBreakIsRaised_OnlyWhenTheRuleSaysSo_ByHowFarOverItWent()
    {
        AssignSchedule(StandardDay);
        await PublishAsync(rules =>
        {
            var rule = rules.Single(r => r.Finding == "LongBreak");
            rule.IsEnabled = true;
            rule.Conditions = [new ConditionDto { Fact = "MinutesOver", Operator = "GreaterOrEqual", Value = 30 }];
        });
        Clock.Local = Monday.AddDays(8).At(0, 0);
        var nextMonday = Monday.AddDays(7);
        foreach (var (date, back) in new[] { (Monday, 45), (nextMonday, 25) })
        {
            AddPunch(NormalizedEventType.CheckIn, date.At(8, 0));
            AddPunch(NormalizedEventType.BreakOut, date.At(12, 0));
            AddPunch(NormalizedEventType.BreakIn, date.At(13, 0).AddMinutes(back));
            AddPunch(NormalizedEventType.CheckOut, date.At(17, 30));
        }

        await ProcessAsync(Monday);
        await ProcessAsync(nextMonday);

        using var db = Database.CreateContext(TestDatabase.TenantA);

        // The break 45 minutes over is raised; the one 25 over is a finding on the day (the policy's allowance is 5) but the rule does not raise it.
        Assert.Equal(Monday, Assert.Single(db.AttendanceExceptions).AttendanceDate);
        Assert.All(db.AttendanceRecords, r => Assert.True(r.BreakFindings.HasFlag(BreakFindings.LongBreak)));
    }

    [Fact]
    public async Task APublishedRuleThatSwitchesInferenceOff_StopsTheBreakBeingRead_WhateverThePolicySays()
    {
        AddDefaultPolicy(p => p.BreakInferenceMode = BreakInferenceMode.FromGap);
        AssignSchedule(StandardDay);
        Clock.Local = Monday.At(18, 0);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(12, 5));
        AddPunch(NormalizedEventType.CheckIn, Monday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));

        await PublishAsync(rules => rules.Single(r => r.Action == "InferBreak").IsEnabled = false);
        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Null(record.ActualBreakOut);
        Assert.Equal(BreakFindings.NoBreak, record.BreakFindings);
    }

    [Fact]
    public async Task TheRecordNotesTheRuleSet_WithoutBecomingANewVersion_AndTheLookBackBringsOlderDaysUpToDate()
    {
        AssignSchedule(StandardDay);
        Clock.Local = Monday.AddDays(1).At(3, 0);
        foreach (var (type, time) in new[] { (NormalizedEventType.CheckIn, 8), (NormalizedEventType.BreakOut, 12), (NormalizedEventType.BreakIn, 13), (NormalizedEventType.CheckOut, 17) })
        {
            AddPunch(type, Monday.At(time, 0));
        }

        await ProcessAsync(Monday);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Equal(0, Assert.Single(db.AttendanceRecords).RuleSetVersion);
        }

        // A set is published; the day is not touched until it is calculated again, and then it is only noted, not rewritten.
        await PublishAsync(_ => { });
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Equal(0, Assert.Single(db.AttendanceRecords).RuleSetVersion);
        }

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var lookback = new AttendanceLookbackService(db, CreateEngine(db), new ScheduleResolutionService(db), new HrAbsenceCalendar(db), CreateAudit(db), Clock, new RuleSetProvider(db), NullLogger<AttendanceLookbackService>.Instance);
            var first = await lookback.RunAsync(30, 2000, CancellationToken.None);
            var second = await lookback.RunAsync(30, 2000, CancellationToken.None);
            Assert.Equal((1, 0), (first.DaysCalculated, second.DaysCalculated));
        }

        using var check = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(check.AttendanceRecords);
        Assert.Equal((1, 1, true), (record.Version, record.RuleSetVersion, record.IsCurrent));
    }

    private AttendanceRuleService Service(RobeemDbContext db) => new(db, CreateAudit(db), new SystemCurrentUser(), Clock);

    /// <summary>Starts a draft of the tenant-wide rules, lets the test change them, and publishes it.</summary>
    private async Task PublishAsync(Action<List<RuleDto>> change)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var service = Service(db);
        var draftId = (await service.CreateDraftAsync(null, CancellationToken.None)).Id!.Value;
        var draft = (await service.GetAsync(draftId, CancellationToken.None))!;
        change(draft.Rules);
        var saved = await service.SaveDraftAsync(draftId, new RulesSaveRequest { Rules = draft.Rules }, CancellationToken.None);
        Assert.True(saved.Succeeded, string.Join("; ", saved.Errors.Select(e => $"{e.Key}: {e.Value}")));
        Assert.True((await service.PublishAsync(draftId, null, CancellationToken.None)).Succeeded);
    }
}
