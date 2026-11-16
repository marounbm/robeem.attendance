using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using robeem.Contracts.Attendance;
using robeem.Web.ApiClients;
using robeem.Web.Controllers;
using robeem.Web.Models;

namespace robeem.Tests.Web;

public sealed class RulesWebTests
{
    private readonly FakeRules _api = new();

    private RulesController? LastController { get; set; }

    [Fact]
    public async Task AddingARule_AddsOneAtTheEnd_AndShowsTheListAgainWithoutSavingIt()
    {
        var (result, model) = await EditAsync(Model(Rule("A"), Rule("B")), "add-rule");

        Assert.IsType<ViewResult>(result);
        Assert.Equal(["A", "B", "New rule"], model.Rules.Select(r => r.Name));
        Assert.Null(_api.Saved);
    }

    [Fact]
    public async Task RulesAreMovedAndRemoved_ByThePositionInTheCommand()
    {
        var (_, up) = await EditAsync(Model(Rule("A"), Rule("B"), Rule("C")), "up:2");
        Assert.Equal(["A", "C", "B"], up.Rules.Select(r => r.Name));

        var (_, down) = await EditAsync(Model(Rule("A"), Rule("B"), Rule("C")), "down:0");
        Assert.Equal(["B", "A", "C"], down.Rules.Select(r => r.Name));

        var (_, removed) = await EditAsync(Model(Rule("A"), Rule("B"), Rule("C")), "remove-rule:1");
        Assert.Equal(["A", "C"], removed.Rules.Select(r => r.Name));

        // Moving the first rule up, or a position that is not there, changes nothing.
        var (_, same) = await EditAsync(Model(Rule("A"), Rule("B")), "up:0");
        var (_, none) = await EditAsync(Model(Rule("A"), Rule("B")), "remove-rule:9");
        Assert.Equal(["A", "B"], same.Rules.Select(r => r.Name));
        Assert.Equal(["A", "B"], none.Rules.Select(r => r.Name));
    }

    [Fact]
    public async Task ConditionsAreAddedAndRemoved_OnTheRuleNamed()
    {
        var rule = Rule("A");
        rule.Conditions = [new ConditionDto { Fact = "MinutesOver", Operator = "Equals", Value = 1 }, new ConditionDto { Fact = "ShiftHasBreak", Operator = "Equals", Value = 1 }];

        var (_, added) = await EditAsync(Model(rule, Rule("B")), "add-condition:1");
        Assert.Equal((2, 1), (added.Rules[0].Conditions.Count, added.Rules[1].Conditions.Count));
        Assert.Equal("MinutesOver", added.Rules[1].Conditions[0].Fact);

        var (_, removed) = await EditAsync(Model(rule), "remove-condition:0:0");
        Assert.Equal(["ShiftHasBreak"], removed.Rules[0].Conditions.Select(c => c.Fact));
    }

    [Fact]
    public async Task TheStageAndFinding_FollowTheAction_AndAReasonBelongsOnlyToASuppression()
    {
        var infer = Rule("Infer");
        (infer.Action, infer.Stage, infer.Finding, infer.Reason) = ("InferBreak", "Exception", "NoShow", "left over");
        var raise = Rule("Raise");
        raise.Reason = "left over";
        var suppress = Rule("Suppress");
        (suppress.Action, suppress.Reason) = ("SuppressException", "Casual staff.");

        var (_, model) = await EditAsync(Model(infer, raise, suppress), "add-rule");

        Assert.Equal(("Interpretation", null, null), (model.Rules[0].Stage, model.Rules[0].Finding, model.Rules[0].Reason));
        Assert.Equal(("Exception", "NoShow", null), (model.Rules[1].Stage, model.Rules[1].Finding, model.Rules[1].Reason));
        Assert.Equal(("Exception", "Casual staff."), (model.Rules[2].Stage, model.Rules[2].Reason));
    }

    [Fact]
    public async Task Save_SendsTheRulesInOrder_AndGoesBackToTheDraft()
    {
        var (result, _) = await EditAsync(Model(Rule("A"), Rule("B")), "save");

        Assert.Equal("Edit", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal(["A", "B"], _api.Saved!.Rules.Select(r => r.Name));
        Assert.Equal(0, _api.Published);
    }

    [Fact]
    public async Task Publish_SavesFirst_ThenPublishes_AndGoesToTheRules()
    {
        var (result, _) = await EditAsync(Model(Rule("A")), "publish");

        Assert.Equal("Advanced", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal((1, 1), (_api.Saved!.Rules.Count, _api.Published));
    }

    [Fact]
    public async Task ARefusedSave_ShowsTheErrorsOnTheirFields_AndPublishesNothing()
    {
        _api.SaveErrors = new Dictionary<string, string> { ["Rules[0].Name"] = "Give the rule a name.", [string.Empty] = "Something else." };

        var (result, model) = await EditAsync(Model(Rule(string.Empty)), "publish");

        Assert.IsType<ViewResult>(result);
        Assert.Equal(0, _api.Published);
        var controller = LastController!;
        Assert.Equal("Give the rule a name.", controller.ModelState["Rules[0].Name"]!.Errors.Single().ErrorMessage);
        Assert.Single(controller.ModelState[string.Empty]!.Errors);
        Assert.Single(model.Rules);
    }

    [Fact]
    public async Task AnActionFromTheGrid_IsSentForThatRuleAndVersion_AndGoesBackToTheGrid()
    {
        var controller = NewController();

        var result = await controller.Manage(5, 3, 2, RuleChangeOperations.Disable, CancellationToken.None);

        Assert.Equal("Advanced", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal((5, 3, 2, RuleChangeOperations.Disable), (_api.Changed!.TimeScheduleId, _api.Changed.ExpectedVersion, _api.Changed.Position, _api.Changed.Operation));
        Assert.True(controller.TempData.ContainsKey("StatusMessage"));
    }

    [Fact]
    public async Task ARefusedActionFromTheGrid_ShowsWhy()
    {
        _api.ChangeErrors = new Dictionary<string, string> { ["id"] = "The rules were changed since this page was shown." };
        var controller = NewController();

        var result = await controller.Manage(null, 3, 2, RuleChangeOperations.Delete, CancellationToken.None);

        Assert.Equal("Advanced", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("changed since", (string)controller.TempData["ErrorMessage"]!);
    }

    [Fact]
    public async Task ChangingOneRule_SendsItInPlaceOfTheOneAtThatPosition_AndKeepsTheFormOnAnError()
    {
        var controller = NewController();
        var rule = Rule("A");
        rule.Action = "InferBreak";
        rule.Finding = "NoShow";

        var saved = await controller.EditRule(null, 4, 2, new RulesEditViewModel { Rules = [rule] }, "save", CancellationToken.None);

        Assert.Equal("Advanced", Assert.IsType<RedirectToActionResult>(saved).ActionName);
        Assert.Equal((RuleChangeOperations.Replace, 4, 2, "Interpretation"), (_api.Changed!.Operation, _api.Changed.Position, _api.Changed.ExpectedVersion, _api.Changed.Rule!.Stage));
        Assert.Null(_api.Changed.Rule.Finding);

        _api.ChangeErrors = new Dictionary<string, string> { ["Rules[0].Name"] = "Give the rule a name." };
        var refused = await controller.EditRule(null, 4, 2, new RulesEditViewModel { Rules = [Rule(string.Empty)] }, "save", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(refused);
        Assert.Equal(4, Assert.IsType<RulesEditViewModel>(view.Model).Position);
        Assert.Equal("Give the rule a name.", controller.ModelState["Rules[0].Name"]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task ChangingOneRule_AddsAConditionWithoutSaving()
    {
        var controller = NewController();

        var result = await controller.EditRule(null, 0, 1, new RulesEditViewModel { Rules = [Rule("A")] }, "add-condition:0", CancellationToken.None);

        Assert.Single(Assert.IsType<RulesEditViewModel>(Assert.IsType<ViewResult>(result).Model).Rules[0].Conditions);
        Assert.Null(_api.Changed);
    }

    [Fact]
    public async Task TheSimpleSettingsPage_ShowsTheSettingsOfTheScope_AndWhyWhenTheyCantBeShown()
    {
        _api.Settings = new RuleSettingsDto { TimeScheduleId = 3, ScopeTitle = "Night shift", Version = 2, IsStandard = true, Settings = new RuleSettings { LongBreakMinutesOver = 5 } };

        var result = await NewController().Index(3, CancellationToken.None);

        var model = Assert.IsType<RulesSettingsViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(("Night shift", 2, true, 5), (model.ScopeTitle, model.Version, model.IsStandard, model.Settings.LongBreakMinutesOver));

        _api.Settings = null;
        Assert.IsType<NotFoundResult>(await NewController().Index(99, CancellationToken.None));
    }

    [Fact]
    public async Task SavingTheSettings_SendsThemWithTheVersionSeen_AndGoesBackToTheSimplePage()
    {
        _api.Settings = new RuleSettingsDto { Version = 2, IsStandard = true, Settings = new RuleSettings { LongBreakMinutesOver = 5 } };
        var controller = NewController();

        var result = await controller.SaveSettings(null, 2, new RulesSettingsViewModel { Settings = new RuleSettings { LongBreakMinutesOver = 8 } }, CancellationToken.None);

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal((2, 8), (_api.SavedSettings!.ExpectedVersion, _api.SavedSettings.Settings.LongBreakMinutesOver));
        Assert.Contains("new version", (string)controller.TempData["StatusMessage"]!);
    }

    [Fact]
    public async Task SavingSettingsThatAreTheSame_SaysSo_WithoutPublishingAVersion()
    {
        _api.Settings = new RuleSettingsDto { Version = 2, IsStandard = true, Settings = new RuleSettings { LongBreakMinutesOver = 5 } };
        var controller = NewController();

        var result = await controller.SaveSettings(null, 2, new RulesSettingsViewModel { Settings = new RuleSettings { LongBreakMinutesOver = 5 } }, CancellationToken.None);

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Null(_api.SavedSettings);
        Assert.Equal("Nothing was changed.", (string)controller.TempData["StatusMessage"]!);
    }

    [Fact]
    public async Task ARefusedSettingsSave_ShowsTheErrorsOnTheirFields_AndKeepsWhatWasTyped()
    {
        _api.Settings = new RuleSettingsDto { Version = 2, IsStandard = true, Settings = new RuleSettings { LongBreakMinutesOver = 5 } };
        _api.SettingsErrors = new Dictionary<string, string> { ["Settings.LongBreakMinutesOver"] = "Use between 0 and 1440 minutes." };
        var controller = NewController();

        var result = await controller.SaveSettings(null, 2, new RulesSettingsViewModel { Settings = new RuleSettings { LongBreakMinutesOver = 5000 } }, CancellationToken.None);

        var model = Assert.IsType<RulesSettingsViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(5000, model.Settings.LongBreakMinutesOver);
        Assert.Equal("Use between 0 and 1440 minutes.", controller.ModelState["Settings.LongBreakMinutesOver"]!.Errors.Single().ErrorMessage);
    }

    private static RulesEditViewModel Model(params RuleDto[] rules) => new() { Rules = [.. rules] };

    private static RuleDto Rule(string name) => new() { Name = name, Stage = "Exception", Action = "RaiseException", Finding = "NoShow", IsEnabled = true };

    private RulesController NewController() => new(_api) { TempData = new TempDataDictionary(new DefaultHttpContext(), new NoTempData()) };

    private async Task<(IActionResult Result, RulesEditViewModel Model)> EditAsync(RulesEditViewModel model, string command)
    {
        var controller = new RulesController(_api) { TempData = new TempDataDictionary(new DefaultHttpContext(), new NoTempData()) };
        LastController = controller;
        var result = await controller.Edit(7, null, model, command, CancellationToken.None);
        return (result, model);
    }

    private sealed class NoTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }

    private sealed class FakeRules : IRulesApiClient
    {
        public RulesSaveRequest? Saved { get; private set; }

        public int Published { get; private set; }

        public Dictionary<string, string>? SaveErrors { get; set; }

        public RuleChangeRequest? Changed { get; private set; }

        public Dictionary<string, string>? ChangeErrors { get; set; }

        public RuleSettingsDto? Settings { get; set; }

        public RuleSettingsSaveRequest? SavedSettings { get; private set; }

        public Dictionary<string, string>? SettingsErrors { get; set; }

        public Task<RuleSettingsDto?> GetSettingsAsync(int? scheduleId, CancellationToken cancellationToken) => Task.FromResult(Settings);

        public Task<ApiSaveResult> SaveSettingsAsync(RuleSettingsSaveRequest request, CancellationToken cancellationToken)
        {
            if (SettingsErrors is not null)
            {
                return Task.FromResult(ApiSaveResult.Invalid(SettingsErrors));
            }

            SavedSettings = request;
            return Task.FromResult(ApiSaveResult.Success(null));
        }

        public Task<IReadOnlyList<RuleScopeDto>> ListScopesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RuleScopeDto>>([]);

        public Task<RuleSetDto?> GetCurrentAsync(int? scheduleId, CancellationToken cancellationToken) => Task.FromResult<RuleSetDto?>(null);

        public Task<RuleSetDto?> GetDraftAsync(int? scheduleId, CancellationToken cancellationToken) => Task.FromResult<RuleSetDto?>(new RuleSetDto { Id = 7, ScopeTitle = "The whole company" });

        public Task<RuleSetDto?> GetAsync(int id, CancellationToken cancellationToken) => Task.FromResult<RuleSetDto?>(null);

        public Task<IReadOnlyList<RuleSetSummaryDto>> ListHistoryAsync(int? scheduleId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RuleSetSummaryDto>>([]);

        public Task<ApiSaveResult> CreateDraftAsync(int? scheduleId, CancellationToken cancellationToken) => Task.FromResult(ApiSaveResult.Success(7));

        public Task<ApiSaveResult> SaveDraftAsync(int draftId, RulesSaveRequest request, CancellationToken cancellationToken)
        {
            if (SaveErrors is not null)
            {
                return Task.FromResult(ApiSaveResult.Invalid(SaveErrors));
            }

            Saved = request;
            return Task.FromResult(ApiSaveResult.Success(draftId));
        }

        public Task<ApiSaveResult> PublishAsync(int draftId, string? notes, CancellationToken cancellationToken)
        {
            Published++;
            return Task.FromResult(ApiSaveResult.Success(draftId));
        }

        public Task<ApiSaveResult> ChangeRuleAsync(RuleChangeRequest request, CancellationToken cancellationToken)
        {
            if (ChangeErrors is not null)
            {
                return Task.FromResult(ApiSaveResult.Invalid(ChangeErrors));
            }

            Changed = request;
            return Task.FromResult(ApiSaveResult.Success(null));
        }

        public Task<ApiSaveResult> DiscardDraftAsync(int draftId, CancellationToken cancellationToken) => Task.FromResult(ApiSaveResult.Success(draftId));
    }
}
