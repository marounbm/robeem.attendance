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

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
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

    private static RulesEditViewModel Model(params RuleDto[] rules) => new() { Rules = [.. rules] };

    private static RuleDto Rule(string name) => new() { Name = name, Stage = "Exception", Action = "RaiseException", Finding = "NoShow", IsEnabled = true };

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

        public Task<ApiSaveResult> DiscardDraftAsync(int draftId, CancellationToken cancellationToken) => Task.FromResult(ApiSaveResult.Success(draftId));
    }
}
