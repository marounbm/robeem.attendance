using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>Attendance policies (screens phase 4): one default, at most one per schedule, validated and audited.</summary>
public sealed class AttendancePolicyServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public AttendancePolicyServiceTests()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        db.TimeSchedules.AddRange(
            new TimeSchedule { Title = "Night shift", CyclePeriod = 7, FirstCycleDay = DayOfWeek.Monday },
            new TimeSchedule { Title = "Day shift", CyclePeriod = 7, FirstCycleDay = DayOfWeek.Monday });
        db.SaveChanges();
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task TheDefaultAndAScheduleOverride_AreCreated_AndEachScopeOnlyOnce()
    {
        Assert.True((await CreateAsync(Input("Default"))).Succeeded);
        Assert.True((await CreateAsync(Input("Day", ScheduleId("Day shift")))).Succeeded);

        var secondDefault = await CreateAsync(Input("Another default"));
        var secondForSchedule = await CreateAsync(Input("Another day", ScheduleId("Day shift")));

        Assert.Contains("exists already", secondDefault.Errors[nameof(PolicyInput.TimeScheduleId)]);
        Assert.Contains("has a policy already", secondForSchedule.Errors[nameof(PolicyInput.TimeScheduleId)]);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(2, db.AttendancePolicies.Count());
    }

    [Fact]
    public async Task APolicyForAnUnknownSchedule_IsRefused()
    {
        var result = await CreateAsync(Input("Ghost", 999));

        Assert.Contains("doesn't exist", result.Errors[nameof(PolicyInput.TimeScheduleId)]);
    }

    [Fact]
    public async Task EverySettingIsStored_AndReadBackAsTheInput()
    {
        var input = Input("Strict", ScheduleId("Night shift"));
        input.RoundingRuleMinutes = 15;
        input.LateGraceMinutes = 5;
        input.EarlyDepartureGraceMinutes = 10;
        input.OvertimeThresholdMinutes = 30;
        input.RequiresOvertimePreApproval = true;
        input.CorrectionApprovalWorkflow = "Both";
        input.UnlabeledPunchInterpretationMode = "Sequential";
        input.DuplicateTimeWindowSeconds = 60;
        input.MissingPunchGraceMinutes = 45;

        var id = (await CreateAsync(input)).Id!.Value;

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var back = await Service(db).GetInputAsync(id, CancellationToken.None);
        Assert.Equal(
            (15, 5, 10, 30, true, "Both", "Sequential", 60, 45),
            (back!.RoundingRuleMinutes, back.LateGraceMinutes, back.EarlyDepartureGraceMinutes, back.OvertimeThresholdMinutes, back.RequiresOvertimePreApproval, back.CorrectionApprovalWorkflow, back.UnlabeledPunchInterpretationMode, back.DuplicateTimeWindowSeconds, back.MissingPunchGraceMinutes));
        var stored = db.AttendancePolicies.Single();
        Assert.Equal((CorrectionApprovalWorkflow.Both, UnlabeledPunchInterpretationMode.Sequential), (stored.CorrectionApprovalWorkflow, stored.UnlabeledPunchInterpretationMode));
    }

    [Theory]
    [InlineData(nameof(PolicyInput.RoundingRuleMinutes), -1)]
    [InlineData(nameof(PolicyInput.RoundingRuleMinutes), 61)]
    [InlineData(nameof(PolicyInput.LateGraceMinutes), 241)]
    [InlineData(nameof(PolicyInput.EarlyDepartureGraceMinutes), -5)]
    [InlineData(nameof(PolicyInput.OvertimeThresholdMinutes), 481)]
    [InlineData(nameof(PolicyInput.DuplicateTimeWindowSeconds), 0)]
    [InlineData(nameof(PolicyInput.DuplicateTimeWindowSeconds), 3601)]
    [InlineData(nameof(PolicyInput.MissingPunchGraceMinutes), 1441)]
    public async Task ASettingOutsideItsRange_IsRefusedByName(string field, int value)
    {
        var input = Input("Bad");
        typeof(PolicyInput).GetProperty(field)!.SetValue(input, value);

        var result = await CreateAsync(input);

        Assert.Contains("Use between", result.Errors[field]);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendancePolicies);
    }

    [Fact]
    public async Task ABlankName_AnUnknownWorkflow_AndAModeWithoutAnEngine_AreRefused()
    {
        var input = Input("  ");
        input.CorrectionApprovalWorkflow = "Nobody";
        input.UnlabeledPunchInterpretationMode = "RulesEngine";

        var result = await CreateAsync(input);

        Assert.Equal(
            [nameof(PolicyInput.Title), nameof(PolicyInput.CorrectionApprovalWorkflow), nameof(PolicyInput.UnlabeledPunchInterpretationMode)],
            result.Errors.Keys.OrderBy(k => k == nameof(PolicyInput.Title) ? 0 : k == nameof(PolicyInput.CorrectionApprovalWorkflow) ? 1 : 2));
    }

    [Fact]
    public async Task Updating_ChangesTheSettings_AuditsBeforeAndAfter_AndKeepsTheScope()
    {
        var id = (await CreateAsync(Input("Default"))).Id!.Value;
        var change = Input("Default");
        change.LateGraceMinutes = 10;
        change.RequiresOvertimePreApproval = true;

        var result = await UpdateAsync(id, change);

        Assert.True(result.Succeeded);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var policy = db.AttendancePolicies.Single();
        Assert.Equal((10, true, (int?)null), (policy.LateGraceMinutes, policy.RequiresOvertimePreApproval, policy.TimeScheduleId));
        var entry = db.AttendanceAuditLogs.ToList().Last();
        Assert.Equal((AttendanceAuditActions.PolicyChanged, nameof(AttendancePolicy), id), (entry.Action, entry.TargetType, entry.TargetId));
        Assert.Contains("Before: rounding 0 min, late grace 0 min", entry.Details);
        Assert.Contains("After: rounding 0 min, late grace 10 min", entry.Details);
    }

    [Fact]
    public async Task APolicy_CanNotMoveToAnotherSchedule_NorBeFoundWhenUnknown()
    {
        var id = (await CreateAsync(Input("Day", ScheduleId("Day shift")))).Id!.Value;

        var moved = await UpdateAsync(id, Input("Day", ScheduleId("Night shift")));
        var toDefault = await UpdateAsync(id, Input("Day"));

        Assert.Contains("can't move", moved.Errors[nameof(PolicyInput.TimeScheduleId)]);
        Assert.Contains("can't move", toDefault.Errors[nameof(PolicyInput.TimeScheduleId)]);
        Assert.True((await UpdateAsync(999, Input("x"))).NotFound);
        Assert.Null(await GetInputAsync(999));
    }

    [Fact]
    public async Task ASchedulesPolicy_CanBeDeleted_ButTheDefaultCanNot()
    {
        var defaultId = (await CreateAsync(Input("Default"))).Id!.Value;
        var scheduleId = (await CreateAsync(Input("Day", ScheduleId("Day shift")))).Id!.Value;

        var refused = await DeleteAsync(defaultId);
        var deleted = await DeleteAsync(scheduleId);

        Assert.Contains("can't be deleted", refused.Errors["id"]);
        Assert.True(deleted.Succeeded);
        Assert.True((await DeleteAsync(scheduleId)).NotFound);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(defaultId, db.AttendancePolicies.Single().Id);
        Assert.Contains(db.AttendanceAuditLogs.ToList(), a => a.Details!.StartsWith("Deleted 'Day'"));
    }

    [Fact]
    public async Task TheList_PutsTheDefaultFirst_ThenSchedulesByName_AndIsPerTenant()
    {
        await CreateAsync(Input("Night", ScheduleId("Night shift")));
        await CreateAsync(Input("Default"));
        await CreateAsync(Input("Day", ScheduleId("Day shift")));

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var list = await Service(db).ListAsync(CancellationToken.None);
        Assert.Equal(["Default", "Day", "Night"], list.Select(p => p.Title));
        Assert.Equal([true, false, false], list.Select(p => p.IsTenantDefault));
        Assert.Equal([null, "Day shift", "Night shift"], list.Select(p => p.TimeScheduleTitle));

        using var other = _database.CreateContext(TestDatabase.TenantB);
        Assert.Empty(await Service(other).ListAsync(CancellationToken.None));
        Assert.Empty(await Service(other).ListScheduleOptionsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TheScheduleOptions_AreByName()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);

        var options = await Service(db).ListScheduleOptionsAsync(CancellationToken.None);

        Assert.Equal(["Day shift", "Night shift"], options.Select(o => o.Title));
    }

    private static PolicyInput Input(string title, int? scheduleId = null) => new() { Title = title, TimeScheduleId = scheduleId };

    private int ScheduleId(string title)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return db.TimeSchedules.Single(s => s.Title == title).Id;
    }

    private async Task<SaveResult> CreateAsync(PolicyInput input)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await Service(db).CreateAsync(input, CancellationToken.None);
    }

    private async Task<SaveResult> UpdateAsync(int id, PolicyInput input)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await Service(db).UpdateAsync(id, input, CancellationToken.None);
    }

    private async Task<SaveResult> DeleteAsync(int id)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await Service(db).DeleteAsync(id, CancellationToken.None);
    }

    private async Task<PolicyInput?> GetInputAsync(int id)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await Service(db).GetInputAsync(id, CancellationToken.None);
    }

    private AttendancePolicyService Service(RobeemDbContext db) =>
        new(db, new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System));
}
