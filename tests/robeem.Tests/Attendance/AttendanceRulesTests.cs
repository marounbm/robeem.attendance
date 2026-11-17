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
        Assert.False(rules.Raises(RuleFinding.LateBreak, new RuleFacts { MinutesOver = 60 }));
        Assert.False(rules.Raises(RuleFinding.EarlyBreak, new RuleFacts { MinutesOver = 60 }));
        Assert.False(rules.Raises(RuleFinding.NoBreak, new RuleFacts()));
        Assert.False(rules.CanInferBreak);

        // The policy's numbers are conditions of the rules: a long break is over 5 minutes past the scheduled break, a late or early one is
        // over 15 minutes from its start, and a no-show waits 120 minutes after the shift ends.
        Assert.False(rules.Raises(RuleFinding.LongBreak, new RuleFacts { MinutesOver = 5 }));
        Assert.True(rules.Raises(RuleFinding.LongBreak, new RuleFacts { MinutesOver = 6 }));
        Assert.True(rules.Notes(RuleFinding.LateBreak, new RuleFacts { MinutesOver = 16 }));
        Assert.False(rules.Notes(RuleFinding.LateBreak, new RuleFacts { MinutesOver = 15 }));
        Assert.False(rules.Raises(RuleFinding.NoShow, new RuleFacts { MinutesAfterShiftEnd = 119 }));
        Assert.True(rules.Raises(RuleFinding.NoShow, new RuleFacts { MinutesAfterShiftEnd = 120 }));
        var custom = RuleSetSnapshot.FromPolicy(new AttendancePolicy { LongBreakGraceMinutes = 10, BreakStartGraceMinutes = 30, MissingPunchGraceMinutes = 45, RaiseExceptionOnLongBreak = true });
        Assert.False(custom.Raises(RuleFinding.LongBreak, new RuleFacts { MinutesOver = 10 }));
        Assert.True(custom.Raises(RuleFinding.LongBreak, new RuleFacts { MinutesOver = 11 }));
        Assert.True(custom.Notes(RuleFinding.EarlyBreak, new RuleFacts { MinutesOver = 31 }));
        Assert.True(custom.Raises(RuleFinding.NoShow, new RuleFacts { MinutesAfterShiftEnd = 45 }));

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
        Assert.Equal(9, current.Rules.Count);
    }

    [Fact]
    public async Task ARuleSet_IsADraftFirst_ThenPublishedAsTheNextVersion_AndThePreviousOneIsRetired()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var service = Service(db);

        var current = (await service.GetCurrentAsync(null, CancellationToken.None))!;
        Assert.Equal(("Policy", 0, 9, null), (current.Status, current.Version, current.Rules.Count, current.Id));
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
        Assert.Contains("Rules[1].Conditions[1].Fact", refused.Errors.Keys);

        // The long-break rule starts with the policy's allowance as its one condition.
        Assert.Equal("MinutesOver", Assert.Single((await service.GetAsync(draftId, CancellationToken.None))!.Rules[1].Conditions).Fact);

        draft.Rules[1].Conditions[1] = new ConditionDto { Fact = "MinutesOver", Operator = "GreaterOrEqual", Value = 30 };
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
            var index = rules.IndexOf(rule);

            // The rule raises from 30 minutes over; a rule that is off after it keeps noting a long break from 5 minutes over, as the policy did.
            rules.Insert(index + 1, new RuleDto
            {
                Name = "Note a long break",
                Stage = "Exception",
                Action = "RaiseException",
                Finding = "LongBreak",
                IsEnabled = false,
                Conditions = [new ConditionDto { Fact = "MinutesOver", Operator = "GreaterThan", Value = 5 }],
            });
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

    [Fact]
    public async Task OneRuleCanBeSwitchedOff_MovedChangedAndDeleted_EachPublishedAsTheNextVersionWithTheOthersAsTheyWere()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var service = Service(db);
        var start = (await service.GetCurrentAsync(null, CancellationToken.None))!;
        var names = start.Rules.Select(r => r.Name).ToList();
        Assert.Equal(0, start.Version);
        var p = start.Rules.FindIndex(r => r.IsEnabled);
        Assert.InRange(p, 0, start.Rules.Count - 2);

        // Switch the first rule that is on off: the starting rules are published as version 1 with that one change.
        var off = await service.ChangeRuleAsync(new RuleChangeRequest { Operation = RuleChangeOperations.Disable, Position = p }, CancellationToken.None);
        Assert.True(off.Succeeded);
        var v1 = (await service.GetCurrentAsync(null, CancellationToken.None))!;
        Assert.Equal(("Published", 1), (v1.Status, v1.Version));
        Assert.Equal(names, v1.Rules.Select(r => r.Name));
        Assert.Equal(start.Rules.Select((r, i) => i != p && r.IsEnabled), v1.Rules.Select(r => r.IsEnabled));
        Assert.Contains($"Switched off rule {p + 1}", v1.Notes);
        Assert.Contains(db.AttendanceAuditLogs, a => a.Action == AttendanceAuditActions.RuleSetPublished && a.Details!.Contains("version 1 published") && a.Details.Contains($"Switched off rule {p + 1}"));

        // A change made against the version before is refused, and so is a change that changes nothing.
        var stale = await service.ChangeRuleAsync(new RuleChangeRequest { Operation = RuleChangeOperations.Disable, Position = 2, ExpectedVersion = 0 }, CancellationToken.None);
        Assert.Contains("changed since", stale.Errors["id"]);
        var again = await service.ChangeRuleAsync(new RuleChangeRequest { Operation = RuleChangeOperations.Disable, Position = p, ExpectedVersion = 1 }, CancellationToken.None);
        Assert.Contains("off already", again.Errors["id"]);

        // Move it down; the first rule can't go earlier.
        Assert.True((await service.ChangeRuleAsync(new RuleChangeRequest { Operation = RuleChangeOperations.MoveDown, Position = p, ExpectedVersion = 1 }, CancellationToken.None)).Succeeded);
        var moved = (await service.GetCurrentAsync(null, CancellationToken.None))!;
        Assert.Equal((2, names[p]), (moved.Version, moved.Rules[p + 1].Name));
        Assert.Equal(names[p + 1], moved.Rules[p].Name);
        Assert.False(moved.Rules[p + 1].IsEnabled);
        var first = await service.ChangeRuleAsync(new RuleChangeRequest { Operation = RuleChangeOperations.MoveUp, Position = 0, ExpectedVersion = 2 }, CancellationToken.None);
        Assert.Contains("first already", first.Errors["id"]);

        // Change one rule: a refused one publishes nothing, with the error keyed as on the form.
        var edited = moved.Rules[0];
        edited.Name = " ";
        var refused = await service.ChangeRuleAsync(new RuleChangeRequest { Operation = RuleChangeOperations.Replace, Position = 0, ExpectedVersion = 2, Rule = edited }, CancellationToken.None);
        Assert.Contains("Rules[0].Name", refused.Errors.Keys);
        Assert.Equal(2, (await service.GetCurrentAsync(null, CancellationToken.None))!.Version);
        edited.Name = "Read the break from the gap";
        Assert.True((await service.ChangeRuleAsync(new RuleChangeRequest { Operation = RuleChangeOperations.Replace, Position = 0, ExpectedVersion = 2, Rule = edited, Notes = "Renamed." }, CancellationToken.None)).Succeeded);
        var renamed = (await service.GetCurrentAsync(null, CancellationToken.None))!;
        Assert.Equal((3, "Read the break from the gap", "Renamed."), (renamed.Version, renamed.Rules[0].Name, renamed.Notes));

        // Delete one; the rest keep their order.
        var count = renamed.Rules.Count;
        Assert.True((await service.ChangeRuleAsync(new RuleChangeRequest { Operation = RuleChangeOperations.Delete, Position = 1, ExpectedVersion = 3 }, CancellationToken.None)).Succeeded);
        var deleted = (await service.GetCurrentAsync(null, CancellationToken.None))!;
        Assert.Equal(count - 1, deleted.Rules.Count);
        Assert.Equal(renamed.Rules.Where((_, i) => i != 1).Select(r => r.Name), deleted.Rules.Select(r => r.Name));

        // Nothing was edited: every version is in the history, the earlier ones retired.
        var history = await service.ListHistoryAsync(null, CancellationToken.None);
        Assert.Equal([(4, "Published"), (3, "Retired"), (2, "Retired"), (1, "Retired")], history.Select(h => (h.Version, h.Status)));
        Assert.Equal(start.Rules.Count, history[^1].RuleCount);

        var nowhere = await service.ChangeRuleAsync(new RuleChangeRequest { Operation = RuleChangeOperations.Delete, Position = 99, ExpectedVersion = 4 }, CancellationToken.None);
        Assert.Contains("no such rule", nowhere.Errors["id"]);
        var unknown = await service.ChangeRuleAsync(new RuleChangeRequest { Operation = "Explode", Position = 0, ExpectedVersion = 4 }, CancellationToken.None);
        Assert.Contains(nameof(RuleChangeRequest.Operation), unknown.Errors.Keys);
    }

    [Fact]
    public async Task ARuleCannotBeChangedOneByOne_WhileTheScopeHasADraft()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var service = Service(db);
        await service.CreateDraftAsync(null, CancellationToken.None);

        var result = await service.ChangeRuleAsync(new RuleChangeRequest { Operation = RuleChangeOperations.Disable, Position = 1 }, CancellationToken.None);

        Assert.Contains("draft", result.Errors["id"]);
        Assert.Empty(await service.ListHistoryAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task HowLongANoShowWaits_IsAConditionOfItsRule_NotThePolicysMissingPunchGrace()
    {
        AssignSchedule(StandardDay);
        await PublishAsync(rules => rules.Single(r => r.Finding == "NoShow").Conditions = [new ConditionDto { Fact = "MinutesAfterShiftEnd", Operator = "GreaterOrEqual", Value = 30 }]);

        // The shift ends at 17:00 and the policy waits two hours, but the rule waits half an hour.
        Clock.Local = Monday.At(17, 29);
        await ProcessAsync(Monday);
        using (var early = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Empty(early.AttendanceExceptions);
        }

        Clock.Local = Monday.At(17, 30);
        await ProcessAsync(Monday);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceExceptionType.NoShow, Assert.Single(db.AttendanceExceptions).ExceptionType);
    }

    [Fact]
    public async Task ABreakThresholdIsAConditionOfTheRule_SoARuleCanBeMoreSensitiveThanThePolicyWas()
    {
        AssignSchedule(StandardDay);
        await PublishAsync(rules =>
        {
            var rule = rules.Single(r => r.Finding == "LongBreak");
            rule.IsEnabled = true;
            rule.Conditions = [new ConditionDto { Fact = "MinutesOver", Operator = "GreaterThan", Value = 2 }];
        });
        Clock.Local = Monday.At(23, 0);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 3));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));

        await ProcessAsync(Monday);

        // Three minutes over: the policy's allowance of 5 would have called it fine.
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceExceptionType.LongBreak, Assert.Single(db.AttendanceExceptions).ExceptionType);
        Assert.True(Assert.Single(db.AttendanceRecords).BreakFindings.HasFlag(BreakFindings.LongBreak));
    }

    [Fact]
    public async Task ASetPublishedBeforeTheThresholdsMovedIntoTheRules_StillUsesThePolicysNumbers_AndShowsThemAsConditions()
    {
        AssignSchedule(StandardDay);
        using (var seed = Database.CreateContext(TestDatabase.TenantA))
        {
            seed.AttendanceRuleSets.Add(new AttendanceRuleSet
            {
                Title = "Rules version 1",
                Status = RuleSetStatus.Published,
                Version = 1,
                ThresholdsInRules = false,
                Rules =
                {
                    new AttendanceRule { Title = "Long breaks", Priority = 10, IsEnabled = true, Stage = RuleStage.Exception, Action = RuleAction.RaiseException, Finding = RuleFinding.LongBreak },
                },
            });
            await seed.SaveChangesAsync();
        }

        Clock.Local = Monday.AddDays(8).At(0, 0);
        var nextMonday = Monday.AddDays(7);
        foreach (var (date, back) in new[] { (Monday, 3), (nextMonday, 8) })
        {
            AddPunch(NormalizedEventType.CheckIn, date.At(8, 0));
            AddPunch(NormalizedEventType.BreakOut, date.At(12, 0));
            AddPunch(NormalizedEventType.BreakIn, date.At(13, 0).AddMinutes(back));
            AddPunch(NormalizedEventType.CheckOut, date.At(17, 0));
        }

        await ProcessAsync(Monday);
        await ProcessAsync(nextMonday);

        // The rule has no condition of its own, but the policy's allowance of 5 minutes still applies to it, as when it was published.
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(nextMonday, Assert.Single(db.AttendanceExceptions).AttendanceDate);

        var current = (await Service(db).GetCurrentAsync(null, CancellationToken.None))!;
        var condition = Assert.Single(current.Rules.Single(r => r.Finding == "LongBreak").Conditions);
        Assert.Equal(("MinutesOver", "GreaterThan", 5), (condition.Fact, condition.Operator, condition.Value));

        // So is the policy's wait for a missing check-out, which the set had no rule for.
        var wait = current.Rules.Single(r => r.Finding == "MissingCheckOut");
        Assert.Equal(("MinutesAfterShiftEnd", "GreaterOrEqual", 120), (wait.Conditions[0].Fact, wait.Conditions[0].Operator, wait.Conditions[0].Value));

        // The policy's minimum for a break is a rule of the set too, since the set has none of its own.
        var shortBreak = current.Rules.Single(r => r.Action == "IgnoreShortBreak");
        Assert.Equal(("BreakMinutes", "LessThan", 10), (shortBreak.Conditions[0].Fact, shortBreak.Conditions[0].Operator, shortBreak.Conditions[0].Value));
    }

    [Fact]
    public async Task ADraftStartedBeforeTheThresholdsMoved_MustBeSavedAgainBeforeItIsPublished_SoNoThresholdIsDropped()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var draft = new AttendanceRuleSet
        {
            Title = "Draft rules",
            Status = RuleSetStatus.Draft,
            ThresholdsInRules = false,
            Rules =
            {
                new AttendanceRule { Title = "Long breaks", Priority = 10, IsEnabled = true, Stage = RuleStage.Exception, Action = RuleAction.RaiseException, Finding = RuleFinding.LongBreak },
            },
        };
        db.AttendanceRuleSets.Add(draft);
        await db.SaveChangesAsync();
        var service = Service(db);

        var refused = await service.PublishAsync(draft.Id, null, CancellationToken.None);
        Assert.Contains("started before", refused.Errors["id"]);

        // Opened, it shows the threshold; saved, it keeps it; then it can be published.
        var shown = (await service.GetAsync(draft.Id, CancellationToken.None))!;
        Assert.Equal("MinutesOver", Assert.Single(shown.Rules.Single(r => r.Finding == "LongBreak").Conditions).Fact);
        Assert.True((await service.SaveDraftAsync(draft.Id, new RulesSaveRequest { Rules = shown.Rules }, CancellationToken.None)).Succeeded);
        Assert.True((await service.PublishAsync(draft.Id, null, CancellationToken.None)).Succeeded);

        var published = (await service.GetCurrentAsync(null, CancellationToken.None))!;
        Assert.Equal("MinutesOver", Assert.Single(published.Rules.Single(r => r.Finding == "LongBreak").Conditions).Fact);
        Assert.Single(published.Rules, r => r.Action == "IgnoreShortBreak");
    }

    [Fact]
    public void HowLongAgoTheShiftEnded_CanOnlyBeUsedByARuleAboutANoShow()
    {
        RuleDto Rule(string finding) => new()
        {
            Name = "Wait",
            Stage = "Exception",
            Action = "RaiseException",
            Finding = finding,
            Conditions = [new ConditionDto { Fact = "MinutesAfterShiftEnd", Operator = "GreaterOrEqual", Value = 30 }],
        };

        Assert.Empty(RuleValidator.Validate([Rule("NoShow")]));
        Assert.Contains("Rules[0].Conditions[0].Fact", RuleValidator.Validate([Rule("LongBreak")]).Keys);
        Assert.Contains("the shift ended at least 30 minutes ago", RuleSentence.Describe(Rule("NoShow")));
    }

    [Fact]
    public void ABreakIsShort_WhenItsWholeMinutesAreBelowTheRulesLimit_AndAZeroLimitSwitchesTheRuleOff()
    {
        var rules = RuleSetSnapshot.FromPolicy(new AttendancePolicy());
        Assert.True(rules.IgnoresShortBreak(new RuleFacts { BreakMinutes = 9 }));
        Assert.False(rules.IgnoresShortBreak(new RuleFacts { BreakMinutes = 10 }));

        var off = RuleSetSnapshot.FromPolicy(new AttendancePolicy { IgnoreBreaksShorterThanMinutes = 0 });
        Assert.False(off.IgnoresShortBreak(new RuleFacts { BreakMinutes = 0 }));
        Assert.Contains(off.Rules, r => r.Action == RuleAction.IgnoreShortBreak && !r.IsEnabled);
    }

    [Fact]
    public async Task ABreakOfNineMinutesFiftyNineSeconds_IsAToiletVisit_ButTenMinutesIsABreak()
    {
        AssignSchedule(StandardDay);
        Clock.Local = Monday.AddDays(8).At(0, 0);
        var nextMonday = Monday.AddDays(7);
        foreach (var (date, back) in new[] { (Monday, TimeSpan.FromSeconds((9 * 60) + 59)), (nextMonday, TimeSpan.FromMinutes(10)) })
        {
            AddPunch(NormalizedEventType.CheckIn, date.At(8, 0));
            AddPunch(NormalizedEventType.BreakOut, date.At(12, 0));
            AddPunch(NormalizedEventType.BreakIn, date.At(12, 0) + back);
            AddPunch(NormalizedEventType.CheckOut, date.At(17, 0));
        }

        await ProcessAsync(Monday);
        await ProcessAsync(nextMonday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var records = db.AttendanceRecords.OrderBy(r => r.AttendanceDate).ToList();
        Assert.Equal([TimeSpan.Zero, TimeSpan.FromMinutes(10)], records.Select(r => r.BreakDuration));
    }

    [Fact]
    public async Task ThePublishedLimitForAShortBreak_DecidesWhichBreaksAreKept()
    {
        AssignSchedule(StandardDay);
        await PublishAsync(rules => rules.Single(r => r.Action == "IgnoreShortBreak").Conditions = [new ConditionDto { Fact = "BreakMinutes", Operator = "LessThan", Value = 3 }]);
        Clock.Local = Monday.At(18, 0);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.BreakOut, Monday.At(10, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(10, 7));
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));

        await ProcessAsync(Monday);

        // Seven minutes is over the rule's limit of 3, so the first trip is the day's break (the policy's 10 would have ignored it).
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((TimeSpan.FromMinutes(67), Monday.At(10, 0)), (record.BreakDuration, record.ActualBreakOut));
    }

    [Fact]
    public async Task ARuleThatIsOff_LetsEveryBreakBeKept_AndTheLimitAppliesToAGapReadAsABreakToo()
    {
        AddDefaultPolicy(p => p.BreakInferenceMode = BreakInferenceMode.FromGap);
        AssignSchedule(StandardDay);
        Clock.Local = Monday.At(18, 0);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.CheckIn, Monday.At(12, 5));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));

        // With the short-break rule off, the five-minute gap is no toilet visit: it is the one gap near the scheduled break, so it is read as the break.
        await PublishAsync(rules => rules.Single(r => r.Action == "IgnoreShortBreak").IsEnabled = false);
        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((TimeSpan.FromMinutes(5), Monday.At(12, 0), AttendancePunchSource.Inferred), (record.BreakDuration, record.ActualBreakOut, record.ActualBreakOutSource));
    }

    [Fact]
    public void ACountShortBreakRule_CanOnlyLookAtTheBreak_AndAReadTheBreakRuleOnlyAtTheGap()
    {
        RuleDto Rule(string action, string fact) => new()
        {
            Name = "Break",
            Stage = "Interpretation",
            Action = action,
            Conditions = [new ConditionDto { Fact = fact, Operator = "LessThan", Value = 10 }],
        };

        Assert.Empty(RuleValidator.Validate([Rule("IgnoreShortBreak", "BreakMinutes")]));
        Assert.Contains("Rules[0].Conditions[0].Fact", RuleValidator.Validate([Rule("IgnoreShortBreak", "GapMinutes")]).Keys);
        Assert.Contains("Rules[0].Conditions[0].Fact", RuleValidator.Validate([Rule("InferBreak", "BreakMinutes")]).Keys);
        Assert.Contains("Rules[0].Finding", RuleValidator.Validate([new RuleDto { Name = "x", Stage = "Interpretation", Action = "IgnoreShortBreak", Finding = "NoShow" }]).Keys);
        Assert.Contains("Rules[0].Stage", RuleValidator.Validate([new RuleDto { Name = "x", Stage = "Exception", Action = "IgnoreShortBreak" }]).Keys);
        Assert.Equal("When the break lasts less than 10 minutes, count the break as work (its two punches are left out).", RuleSentence.Describe(Rule("IgnoreShortBreak", "BreakMinutes")));
    }

    [Fact]
    public void AMissingCheckOutWaitsAsTheRulesSay_AndIsAlwaysRaisedAfterADay()
    {
        var rules = RuleSetSnapshot.FromPolicy(new AttendancePolicy());
        Assert.False(rules.RaisesMissingCheckOut(new RuleFacts { MinutesAfterShiftEnd = 119 }));
        Assert.True(rules.RaisesMissingCheckOut(new RuleFacts { MinutesAfterShiftEnd = 120 }));

        // Rules that are switched off, or none at all, only delay it: after 24 hours it is raised whatever they say.
        var none = new RuleSetSnapshot(1, "published version 1", []);
        Assert.False(none.RaisesMissingCheckOut(new RuleFacts { MinutesAfterShiftEnd = 1439 }));
        Assert.True(none.RaisesMissingCheckOut(new RuleFacts { MinutesAfterShiftEnd = 1440 }));
        Assert.False(RuleSetSnapshot.FromPolicy(new AttendancePolicy { MissingPunchGraceMinutes = 45 }).RaisesMissingCheckOut(new RuleFacts { MinutesAfterShiftEnd = 44 }));
        Assert.True(RuleSetSnapshot.FromPolicy(new AttendancePolicy { MissingPunchGraceMinutes = 45 }).RaisesMissingCheckOut(new RuleFacts { MinutesAfterShiftEnd = 45 }));
    }

    [Fact]
    public async Task HowLongAMissingCheckOutWaits_IsAConditionOfItsRule_NotThePolicy()
    {
        AssignSchedule(StandardDay);
        await PublishAsync(rules => rules.Single(r => r.Finding == "MissingCheckOut").Conditions = [new ConditionDto { Fact = "MinutesAfterShiftEnd", Operator = "GreaterOrEqual", Value = 30 }]);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));

        // The shift ends at 17:00 and the policy waits two hours, but the rule waits half an hour.
        Clock.Local = Monday.At(17, 29);
        await ProcessAsync(Monday);
        using (var early = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.DoesNotContain(early.AttendanceExceptions, e => e.MissingEventType == NormalizedEventType.CheckOut);
        }

        Clock.Local = Monday.At(17, 30);
        await ProcessAsync(Monday);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Contains(db.AttendanceExceptions, e => e.ExceptionType == AttendanceExceptionType.MissingPunch && e.MissingEventType == NormalizedEventType.CheckOut);
    }

    [Fact]
    public async Task ARuleThatIsOff_CannotKeepAMissingCheckOutFromBeingRaised_BeyondADay()
    {
        AssignSchedule(StandardDay);
        await PublishAsync(rules => rules.Single(r => r.Finding == "MissingCheckOut").IsEnabled = false);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));

        // With its rule off nothing raises it, until the shift ended a day ago.
        Clock.Local = Monday.At(16, 59).AddDays(1);
        await ProcessAsync(Monday);
        using (var before = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.DoesNotContain(before.AttendanceExceptions, e => e.MissingEventType == NormalizedEventType.CheckOut);
        }

        Clock.Local = Monday.At(17, 0).AddDays(1);
        await ProcessAsync(Monday);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Contains(db.AttendanceExceptions, e => e.ExceptionType == AttendanceExceptionType.MissingPunch && e.MissingEventType == NormalizedEventType.CheckOut);
    }

    [Fact]
    public void AMissingCheckOut_CanBeRaisedWithAWait_ButNeverLeftOutByARule()
    {
        RuleDto Rule(string action) => new()
        {
            Name = "Missing check-out",
            Stage = "Exception",
            Action = action,
            Finding = "MissingCheckOut",
            Reason = "Because.",
            Conditions = [new ConditionDto { Fact = "MinutesAfterShiftEnd", Operator = "GreaterOrEqual", Value = 60 }],
        };

        Assert.Empty(RuleValidator.Validate([Rule("RaiseException")]));
        Assert.Contains("Rules[0].Finding", RuleValidator.Validate([Rule("SuppressException")]).Keys);
        Assert.Equal("When the shift ended at least 60 minutes ago, raise an exception for a missing check-out.", RuleSentence.Describe(Rule("RaiseException")));
    }

    [Fact]
    public async Task TheStartingRules_AreShownAsSettings_FromThePolicysNumbers()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);

        var shown = (await Service(db).GetSettingsAsync(null, CancellationToken.None))!;

        Assert.Equal((true, null, 0), (shown.IsStandard, shown.NotStandardReason, shown.Version));
        Assert.Equal(
            new RuleSettings
            {
                LongBreakRaises = false,
                LongBreakMinutesOver = 5,
                LateBreakRaises = false,
                LateBreakMinutesOver = 15,
                EarlyBreakRaises = false,
                EarlyBreakMinutesOver = 15,
                NoBreakRaises = true,
                ShortHoursRaises = true,
                ShortHoursMinutesOver = 0,
                NoShowRaises = true,
                NoShowWaitMinutes = 120,
                MissingCheckOutWaitMinutes = 120,
                ShortBreakIsWork = true,
                ShortBreakMinutes = 10,
                GapIsBreak = false,
                GapWindowMinutes = 90,
                GapMaxMinutes = 120,
            },
            shown.Settings);
    }

    [Fact]
    public async Task SavingSettings_PublishesTheStandardRulesWithThemAsTheNextVersion_AndSaysWhatChanged()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var service = Service(db);
        var shown = (await service.GetSettingsAsync(null, CancellationToken.None))!;
        var settings = shown.Settings with { };
        settings.LongBreakRaises = true;
        settings.LongBreakMinutesOver = 20;
        settings.NoShowWaitMinutes = 30;
        settings.GapIsBreak = true;
        settings.GapWindowMinutes = 45;

        var saved = await service.SaveSettingsAsync(new RuleSettingsSaveRequest { ExpectedVersion = 0, Settings = settings }, CancellationToken.None);

        Assert.True(saved.Succeeded);
        var back = (await service.GetSettingsAsync(null, CancellationToken.None))!;
        Assert.Equal((1, true, settings), (back.Version, back.IsStandard, back.Settings));

        // Only the standard rules changed, in the same order, and the history and the audit log say what was done.
        var rules = (await service.GetCurrentAsync(null, CancellationToken.None))!;
        Assert.Equal(9, rules.Rules.Count);
        Assert.Contains("long break on", rules.Notes);
        Assert.Contains("long break over 5 → 20 min", rules.Notes);
        Assert.Contains(db.AttendanceAuditLogs, a => a.Action == AttendanceAuditActions.RuleSetPublished && a.Details!.Contains("no-show wait 120 → 30 min"));

        // The same settings again change nothing; and a page that is out of date is refused.
        var same = await service.SaveSettingsAsync(new RuleSettingsSaveRequest { ExpectedVersion = 1, Settings = settings }, CancellationToken.None);
        Assert.Equal("Nothing was changed.", same.Errors["id"]);
        var stale = await service.SaveSettingsAsync(new RuleSettingsSaveRequest { ExpectedVersion = 0, Settings = shown.Settings }, CancellationToken.None);
        Assert.Contains("changed since", stale.Errors["id"]);
        Assert.Single(await service.ListHistoryAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task ASettingOutOfRange_IsRefusedOnItsField_AndNothingIsPublished()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var service = Service(db);
        var settings = (await service.GetSettingsAsync(null, CancellationToken.None))!.Settings with { };
        settings.LongBreakMinutesOver = -1;
        settings.GapMaxMinutes = 1441;

        var refused = await service.SaveSettingsAsync(new RuleSettingsSaveRequest { Settings = settings }, CancellationToken.None);

        Assert.Equal(["Settings.GapMaxMinutes", "Settings.LongBreakMinutesOver"], refused.Errors.Keys.Order());
        Assert.Empty(await service.ListHistoryAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task CustomisedRules_CantBeShownAsSettings_NorSaved()
    {
        await PublishAsync(rules => rules.Insert(0, new RuleDto { Name = "Away without notice is fine", Stage = "Exception", Action = "SuppressException", Finding = "NoShow", Reason = "Casual staff." }));
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var service = Service(db);

        var shown = (await service.GetSettingsAsync(null, CancellationToken.None))!;
        Assert.False(shown.IsStandard);
        Assert.Contains("customised", shown.NotStandardReason);
        var refused = await service.SaveSettingsAsync(new RuleSettingsSaveRequest { ExpectedVersion = shown.Version, Settings = new RuleSettings() }, CancellationToken.None);
        Assert.Contains("customised", refused.Errors["id"]);

        // A rule given other conditions, or taken out, is a customisation too.
        Assert.Null(RuleSettingsMapper.TryRead([], out _));
    }

    [Fact]
    public async Task AnOpenDraft_StopsTheSettingsBeingSaved()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var service = Service(db);
        await service.CreateDraftAsync(null, CancellationToken.None);
        var open = (await service.GetSettingsAsync(null, CancellationToken.None))!;

        var refused = await service.SaveSettingsAsync(new RuleSettingsSaveRequest { ExpectedVersion = open.Version, Settings = open.Settings with { LongBreakRaises = true } }, CancellationToken.None);

        Assert.Contains("draft", refused.Errors["id"]);
        Assert.Empty(await service.ListHistoryAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task TheSavedSettings_AreWhatTheCalculationUses()
    {
        AssignSchedule(StandardDay);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var service = Service(db);
            var settings = (await service.GetSettingsAsync(null, CancellationToken.None))!.Settings with { };
            settings.NoShowWaitMinutes = 30;
            Assert.True((await service.SaveSettingsAsync(new RuleSettingsSaveRequest { Settings = settings }, CancellationToken.None)).Succeeded);
        }

        // The shift ends at 17:00; the policy waits two hours, the saved setting half an hour.
        Clock.Local = Monday.At(17, 29);
        await ProcessAsync(Monday);
        using (var early = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Empty(early.AttendanceExceptions);
        }

        Clock.Local = Monday.At(17, 30);
        await ProcessAsync(Monday);
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceExceptionType.NoShow, Assert.Single(check.AttendanceExceptions).ExceptionType);
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
