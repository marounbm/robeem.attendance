using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.Contracts.HR.Employees;
using robeem.Web.Actors;
using robeem.Web.ApiClients;
using robeem.Web.Controllers;
using robeem.Web.Models;
using robeem.Web.Navigation;

namespace robeem.Tests.Web;

/// <summary>The policy and audit screens (screens phase 4): the typed clients and the controllers.</summary>
public sealed class PoliciesWebTests
{
    private static readonly PolicyListItem Default = new() { Id = 1, Title = "Default", IsTenantDefault = true };
    private static readonly PolicyListItem DayPolicy = new() { Id = 2, Title = "Day", TimeScheduleId = 10, TimeScheduleTitle = "Day shift" };

    [Fact]
    public async Task ThePoliciesClient_UsesTheRoutesOfEveryAction_AndMapsA404ToNull()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, "[]");
        var client = new PoliciesApiClient(handler.CreateClient());

        await client.ListAsync(CancellationToken.None);
        await client.ListScheduleOptionsAsync(CancellationToken.None);
        await client.CreateAsync(new PolicyInput { Title = "x" }, CancellationToken.None);
        await client.UpdateAsync(4, new PolicyInput { Title = "x" }, CancellationToken.None);
        await client.DeleteAsync(4, CancellationToken.None);

        Assert.Equal(
            [("GET", "/api/attendance/policies"), ("GET", "/api/attendance/policies/schedule-options"), ("POST", "/api/attendance/policies"), ("PUT", "/api/attendance/policies/4"), ("DELETE", "/api/attendance/policies/4")],
            handler.Requests.Select(r => (r.Method.Method, r.RequestUri!.PathAndQuery)));
        Assert.Null(await new PoliciesApiClient(new StubHttpHandler(HttpStatusCode.NotFound).CreateClient()).GetInputAsync(9, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAndDelete_ReturnTheIdOrTheFieldErrors()
    {
        var problem = """{"title":"x","status":400,"errors":{"TimeScheduleId":["That schedule has a policy already."],"id":["The default can't be deleted."]}}""";
        var created = new PoliciesApiClient(new StubHttpHandler(HttpStatusCode.Created, """{"id":5}""").CreateClient());
        var refused = new PoliciesApiClient(new StubHttpHandler(HttpStatusCode.BadRequest, problem, "application/problem+json").CreateClient());

        var ok = await created.CreateAsync(new PolicyInput(), CancellationToken.None);
        var duplicate = await refused.CreateAsync(new PolicyInput(), CancellationToken.None);
        var delete = await refused.DeleteAsync(1, CancellationToken.None);

        Assert.Equal((true, 5), (ok.Succeeded, ok.Id));
        Assert.Equal("That schedule has a policy already.", duplicate.Errors[nameof(PolicyInput.TimeScheduleId)]);
        Assert.Contains("can't be deleted", delete.Errors[string.Empty]);
    }

    [Fact]
    public async Task TheAuditClient_SendsOnlyTheFiltersThatWereGiven_Escaped()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, """{"items":[{"id":3,"actor":"employee:7","action":"PolicyChanged","entityType":"AttendancePolicy","entityId":2,"timestamp":"2026-10-05T14:03:00+00:00","details":"x"}],"total":41,"page":1,"pageSize":20}""");
        var client = new AuditApiClient(handler.CreateClient());

        var all = await client.ListAsync(null, " ", null, 1, 20, CancellationToken.None);
        await client.ListAsync("Policy Changed", "AttendancePolicy", 2, 3, 50, CancellationToken.None);

        Assert.Equal((3, 41), (all.Items.Single().Id, all.Total));
        Assert.Equal(
            ["/api/attendance/audit?page=1&pageSize=20", "/api/attendance/audit?page=3&pageSize=50&action=Policy%20Changed&entityType=AttendancePolicy&entityId=2"],
            handler.Requests.Select(r => r.RequestUri!.PathAndQuery));
    }

    [Fact]
    public async Task TheNewPolicyForm_OffersTheDefaultOnlyWhenNoneExists_AndOnlySchedulesWithoutAPolicy()
    {
        var withoutDefault = WithTempData(new PoliciesController(new FakePolicies([DayPolicy])));
        var withDefault = WithTempData(new PoliciesController(new FakePolicies([Default, DayPolicy])));

        var first = (PolicyFormViewModel)((ViewResult)await withoutDefault.Create(CancellationToken.None)).Model!;
        var second = (PolicyFormViewModel)((ViewResult)await withDefault.Create(CancellationToken.None)).Model!;

        Assert.Equal((false, (int?)null), (first.DefaultExists, first.Input.TimeScheduleId));
        Assert.Equal([11], first.Schedules.Select(s => s.Id));
        Assert.Equal((true, (int?)11), (second.DefaultExists, second.Input.TimeScheduleId));
        Assert.Equal([11], second.Schedules.Select(s => s.Id));
    }

    [Fact]
    public async Task Creating_RedirectsOnSuccess_AndShowsTheFormWithTheErrorsOtherwise()
    {
        var policies = new FakePolicies([]) { Save = ApiSaveResult.Success(3) };
        var controller = WithTempData(new PoliciesController(policies));
        var model = new PolicyFormViewModel { Input = new PolicyInput { Title = "Default" } };

        var created = await controller.Create(model, CancellationToken.None);
        Assert.IsType<RedirectToActionResult>(created);
        Assert.Contains("'Default' was created", (string)controller.TempData["StatusMessage"]!);

        policies.Save = ApiSaveResult.Invalid(new Dictionary<string, string> { [nameof(PolicyInput.RoundingRuleMinutes)] = "Use between 0 and 60 minutes." });
        var again = await controller.Create(model, CancellationToken.None);

        Assert.IsType<ViewResult>(again);
        Assert.Equal("Use between 0 and 60 minutes.", controller.ModelState["Input.RoundingRuleMinutes"]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task Editing_ShowsTheScope_AndAnUnknownPolicyIsNotFound()
    {
        var policies = new FakePolicies([Default, DayPolicy]);
        var controller = WithTempData(new PoliciesController(policies));

        var edit = (PolicyFormViewModel)((ViewResult)await controller.Edit(2, CancellationToken.None)).Model!;
        var defaultEdit = (PolicyFormViewModel)((ViewResult)await controller.Edit(1, CancellationToken.None)).Model!;

        Assert.Equal(("Schedule: Day shift", "The whole tenant (default)"), (edit.ScopeName, defaultEdit.ScopeName));
        Assert.IsType<NotFoundResult>(await controller.Edit(9, CancellationToken.None));

        policies.Save = ApiSaveResult.Missing();
        Assert.IsType<NotFoundResult>(await controller.Edit(9, new PolicyFormViewModel(), CancellationToken.None));
    }

    [Fact]
    public async Task Deleting_ReportsTheOutcome_AndARefusedDeleteIsAnError()
    {
        var policies = new FakePolicies([Default, DayPolicy]);
        var controller = WithTempData(new PoliciesController(policies));

        await controller.Delete(2, CancellationToken.None);
        Assert.Contains("follows the tenant-wide default", (string)controller.TempData["StatusMessage"]!);

        policies.Save = ApiSaveResult.Invalid(new Dictionary<string, string> { [string.Empty] = "The default can't be deleted." });
        await controller.Delete(1, CancellationToken.None);
        Assert.Equal("The default can't be deleted.", controller.TempData["ErrorMessage"]);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-4, 20)]
    [InlineData(7, 20)]
    [InlineData(501, 20)]
    [InlineData(50, 50)]
    public async Task TheAuditPage_KeepsTheFilters_AndOnlyAsksForAPageSizeItOffers(int pageSize, int expected)
    {
        var audit = new FakeAudit { Total = 95 };
        var controller = new AuditController(audit);

        var model = (AuditIndexViewModel)((ViewResult)await controller.Index("ReviewDecided", "AttendanceRecord", 7, 3, pageSize, CancellationToken.None)).Model!;

        Assert.Equal(("ReviewDecided", "AttendanceRecord", 7, 3, expected), (audit.Asked.Action, audit.Asked.EntityType, audit.Asked.EntityId, audit.Asked.Page, audit.Asked.PageSize));
        Assert.Equal(("ReviewDecided", expected, 3, 95), (model.Action, model.PageSize, model.Page, model.Total));
        Assert.Equal("ReviewDecided", model.Pager.For(2)["action"]);
        Assert.Equal(("7", "AttendanceRecord"), (model.Pager.For(2)["entityId"], model.Pager.For(2)["entityType"]));
    }

    [Fact]
    public async Task TheAuditPage_AsksForAPageBeforeTheFirstAsTheFirst()
    {
        var audit = new FakeAudit();

        await new AuditController(audit).Index(null, null, null, -2, 0, CancellationToken.None);

        Assert.Equal((1, 20), (audit.Asked.Page, audit.Asked.PageSize));
    }

    [Fact]
    public void TheAuditPager_ShowsWhereThePageIsInTheWholeLog()
    {
        var pager = new AuditIndexViewModel { Page = 3, PageSize = 20, Total = 95 }.Pager;

        Assert.Equal((5, 41, 60), (pager.PageCount, pager.FirstItem, pager.LastItem));
    }

    [Fact]
    public void AnInstant_IsWrittenInUtc()
    {
        Assert.Equal("2026-10-05 12:03 UTC", DisplayFormats.Instant(new DateTimeOffset(2026, 10, 5, 14, 3, 0, TimeSpan.FromHours(2))));
    }

    [Fact]
    public void ThePolicyAndAuditScreens_AreForHrAdministratorsOnly()
    {
        var hr = NavigationMenu.For(new FakeActor(["HRAdministrator"])).SelectMany(s => s.Items).Select(i => i.Controller);
        var others = NavigationMenu.For(new FakeActor(["Employee", "Manager", "PayrollAdministrator", "IntegrationAdministrator"])).SelectMany(s => s.Items).Select(i => i.Controller);

        Assert.Contains("Policies", hr);
        Assert.Contains("Audit", hr);
        Assert.DoesNotContain(others, c => c is "Policies" or "Audit");
    }

    private static T WithTempData<T>(T controller)
        where T : Controller
    {
        controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new NullTempDataProvider());
        return controller;
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }

    private sealed class FakeActor : ICurrentActor
    {
        private readonly string[] _roles;

        public FakeActor(string[] roles)
        {
            _roles = roles;
        }

        public bool HasChoice => true;

        public ActingAs? Choice => new(1, "Test", _roles);

        public bool IsInRole(string role) => _roles.Contains(role);

        public bool IsInAnyRole(params string[] roles) => roles.Any(IsInRole);
    }

    private sealed class FakeAudit : IAuditApiClient
    {
        public (string? Action, string? EntityType, int? EntityId, int Page, int PageSize) Asked { get; private set; }

        public int Total { get; set; }

        public Task<PagedResult<AuditLogEntry>> ListAsync(string? action, string? entityType, int? entityId, int page, int pageSize, CancellationToken cancellationToken)
        {
            Asked = (action, entityType, entityId, page, pageSize);
            return Task.FromResult(new PagedResult<AuditLogEntry> { Total = Total, Page = page, PageSize = pageSize });
        }
    }

    private sealed class FakePolicies : IPoliciesApiClient
    {
        private readonly IReadOnlyList<PolicyListItem> _policies;

        public FakePolicies(IReadOnlyList<PolicyListItem> policies)
        {
            _policies = policies;
        }

        public ApiSaveResult Save { get; set; } = ApiSaveResult.Success(1);

        public Task<IReadOnlyList<PolicyListItem>> ListAsync(CancellationToken cancellationToken) => Task.FromResult(_policies);

        public Task<PolicyInput?> GetInputAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult(_policies.Any(p => p.Id == id) ? new PolicyInput { Title = "x" } : null);

        public Task<IReadOnlyList<ScheduleOption>> ListScheduleOptionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScheduleOption>>([new ScheduleOption { Id = 10, Title = "Day shift" }, new ScheduleOption { Id = 11, Title = "Night shift" }]);

        public Task<ApiSaveResult> CreateAsync(PolicyInput input, CancellationToken cancellationToken) => Task.FromResult(Save);

        public Task<ApiSaveResult> UpdateAsync(int id, PolicyInput input, CancellationToken cancellationToken) => Task.FromResult(Save);

        public Task<ApiSaveResult> DeleteAsync(int id, CancellationToken cancellationToken) => Task.FromResult(Save);
    }
}
