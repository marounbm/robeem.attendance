using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using robeem.Contracts.Attendance;
using robeem.Contracts.Security;
using robeem.Web.Actors;
using robeem.Web.ApiClients;
using robeem.Web.Controllers;
using robeem.Web.Models;
using robeem.Web.Navigation;

namespace robeem.Tests.Web;

/// <summary>
/// The Web turns away an acting choice the Api would refuse (a missing role, or no employee for a
/// screen about one's own employee) before calling it, so the user sees why and no exception is thrown.
/// </summary>
public sealed class ActorAccessFilterTests
{
    [Theory]
    [InlineData(typeof(ApprovalsController), "Index", new[] { "Employee" })]
    [InlineData(typeof(PayrollController), "Index", new[] { "HRAdministrator" })]
    [InlineData(typeof(PoliciesController), "Index", new[] { "Manager" })]
    [InlineData(typeof(AuditController), "Index", new[] { "Employee", "Manager" })]
    public void AnActorWithoutTheScreensRoles_IsTurnedAway(Type controller, string action, string[] roles)
    {
        var context = Run(controller, action, employeeId: 1, roles);

        Assert.Equal(403, Assert.IsType<ViewResult>(context.Result).StatusCode);
    }

    [Theory]
    [InlineData(typeof(ApprovalsController), "Index", "Manager")]
    [InlineData(typeof(ApprovalsController), "Decide", "Manager")]
    [InlineData(typeof(ApprovalsController), "Decide", "HRAdministrator")]
    [InlineData(typeof(RecordsController), "Index", "Employee")]
    [InlineData(typeof(RecordsController), "Day", "Manager")]
    [InlineData(typeof(ExceptionsController), "Index", "Employee")]
    [InlineData(typeof(ExceptionsController), "Accept", "HRAdministrator")]
    public void AnActorWithNoEmployee_IsTurnedAwayWithTheReason_WhereTheApiNeedsOne(Type controller, string action, string role)
    {
        var context = Run(controller, action, employeeId: null, [role]);

        var view = Assert.IsType<ViewResult>(context.Result);
        Assert.Equal(("AccessDenied", 403), (view.ViewName, view.StatusCode));
        Assert.Contains("not linked to an employee", (string)view.ViewData[AccessDeniedPage.ReasonKey]!);
    }

    [Theory]
    [InlineData(typeof(ApprovalsController), "Index", "HRAdministrator")]
    [InlineData(typeof(RecordsController), "Index", "HRAdministrator")]
    [InlineData(typeof(RecordsController), "Reprocess", "HRAdministrator")]
    [InlineData(typeof(ExceptionsController), "Index", "HRAdministrator")]
    [InlineData(typeof(EmployeesController), "Index", "HRAdministrator")]
    public void HrWithNoEmployee_MayOpenWhatNeedsNoEmployeeOfTheirOwn(Type controller, string action, string role)
    {
        var context = Run(controller, action, employeeId: null, [role]);

        Assert.Null(context.Result);
    }

    [Fact]
    public void WithoutAChoice_TheApiDecides()
    {
        var context = Run(typeof(ApprovalsController), "Decide", choice: null);

        Assert.Null(context.Result);
    }

    [Fact]
    public void TheMenuAndTheStartPage_HideWhatTheActorCannotOpen()
    {
        string[] Screens(int? employeeId, params string[] roles) =>
            NavigationMenu.For(new FakeActor(new ActingAs(employeeId, "x", roles))).SelectMany(s => s.Items).Select(i => i.Controller).ToArray();
        string[] employeeScreens = ["Records", "Exceptions", "Approvals"];

        var managerWithNoEmployee = Screens(null, "Manager");
        var employeeWithNoEmployee = Screens(null, "Employee");
        var hrWithNoEmployee = Screens(null, "HRAdministrator");
        var managerWithEmployee = Screens(3, "Manager");

        Assert.Empty(managerWithNoEmployee);
        Assert.Empty(employeeWithNoEmployee);
        Assert.All(employeeScreens, screen => Assert.Contains(screen, hrWithNoEmployee));
        Assert.All(employeeScreens, screen => Assert.Contains(screen, managerWithEmployee));
        Assert.DoesNotContain("Employees", managerWithEmployee);
    }

    [Fact]
    public void AMenuScreenThatNeedsAnEmployee_IsOneWhoseControllerRequiresOne_AndTheOtherWayRound()
    {
        var controllers = typeof(ApprovalsController).Assembly.GetTypes().Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract);

        var requiring = controllers
            .Where(t => t.GetCustomAttributes(typeof(RequiresEmployeeAttribute), inherit: true).Length > 0
                || t.GetMethod("Index")?.GetCustomAttributes(typeof(RequiresEmployeeAttribute), inherit: true).Length > 0)
            .Select(t => t.Name[..^"Controller".Length])
            .Order();
        var flagged = NavigationMenu.Sections.SelectMany(s => s.Items).Where(i => i.NeedsEmployee).Select(i => i.Controller).Order();

        Assert.Equal(requiring, flagged);
    }

    [Fact]
    public void WithNoChoice_TheApisStandInUserIsWhoIsChecked()
    {
        var managerOnly = new ActingAs(null, "Development user", ["Manager"]);
        var everyRole = new ActingAs(null, "Development user", [.. ActorProtocol.Roles]);

        var turnedAway = Run(typeof(ApprovalsController), "Index", choice: null, managerOnly);
        var hr = Run(typeof(ApprovalsController), "Index", choice: null, everyRole);

        var view = Assert.IsType<ViewResult>(turnedAway.Result);
        Assert.Contains("the Api's development user is not linked to an employee", (string)view.ViewData[AccessDeniedPage.ReasonKey]!);
        Assert.Null(hr.Result);
    }

    [Fact]
    public async Task TheActor_AsksTheApiOnceForItsStandInUser_AndKeepsTheAnswer()
    {
        var api = new StubStandIn { Answer = new StandInActor { EmployeeId = null, Roles = ["Manager"] } };
        var cache = new StandInCache();
        var actor = new CurrentActor(new HttpContextAccessor { HttpContext = new DefaultHttpContext() }, api, cache);
        var later = new CurrentActor(new HttpContextAccessor { HttpContext = new DefaultHttpContext() }, api, cache);

        Assert.True(actor.IsInRole("HRAdministrator"));
        await actor.ResolveAsync(CancellationToken.None);
        await later.ResolveAsync(CancellationToken.None);

        Assert.Equal(1, api.Calls);
        Assert.False(actor.IsInRole("HRAdministrator"));
        Assert.True(later.IsInRole("Manager"));
        Assert.Null(later.Effective!.EmployeeId);
        Assert.False(later.HasChoice);
    }

    [Fact]
    public async Task TheActor_StaysUnknown_WhenTheApiCannotBeAsked_AndAsksAgainNextTime()
    {
        var api = new StubStandIn { Fails = true };
        var cache = new StandInCache();
        var actor = new CurrentActor(new HttpContextAccessor { HttpContext = new DefaultHttpContext() }, api, cache);

        await actor.ResolveAsync(CancellationToken.None);
        api.Fails = false;
        await actor.ResolveAsync(CancellationToken.None);

        Assert.Equal(2, api.Calls);
        Assert.True(cache.Known);
        Assert.Null(actor.Effective);
    }

    [Theory]
    [InlineData(typeof(PayloadFormatsController), "Create")]
    [InlineData(typeof(PayloadFormatsController), "Edit")]
    [InlineData(typeof(PayloadFormatsController), "Preview")]
    [InlineData(typeof(PayloadFormatsController), "Sample")]
    [InlineData(typeof(VendorsController), "Index")]
    [InlineData(typeof(ConnectorsController), "Create")]
    [InlineData(typeof(TransportsController), "Index")]
    [InlineData(typeof(DeviceTypesController), "Index")]
    public void TheCatalogIsChangedInThePrimeTenantOnly_EvenByAPlatformAdministrator(Type controller, string action)
    {
        var elsewhere = RunInTenant(controller, action, ["PlatformAdministrator", "IntegrationAdministrator"], isPrime: false);
        var inPrime = RunInTenant(controller, action, ["PlatformAdministrator"], isPrime: true);
        var unknown = RunInTenant(controller, action, ["PlatformAdministrator"], isPrime: null);

        var view = Assert.IsType<ViewResult>(elsewhere.Result);
        Assert.Equal(("AccessDenied", 403), (view.ViewName, view.StatusCode));
        Assert.Contains("prime tenant", (string)view.ViewData[AccessDeniedPage.ReasonKey]!);
        Assert.Null(inPrime.Result);

        // While the tenant is not known, the Api decides.
        Assert.Null(unknown.Result);
    }

    [Theory]
    [InlineData(typeof(PayloadFormatsController), "Create")]
    [InlineData(typeof(PayloadFormatsController), "Edit")]
    [InlineData(typeof(VendorsController), "Index")]
    public void AnIntegrationAdministrator_WhoIsNotAPlatformAdministrator_CanNotChangeTheCatalog_EvenInThePrimeTenant(Type controller, string action)
    {
        var context = RunInTenant(controller, action, ["IntegrationAdministrator"], isPrime: true);

        Assert.Equal(403, Assert.IsType<ViewResult>(context.Result).StatusCode);
    }

    [Theory]
    [InlineData(typeof(PayloadFormatsController), "Index")]
    [InlineData(typeof(PayloadFormatsController), "Details")]
    [InlineData(typeof(PayloadFormatsController), "Download")]
    public void EveryTenantsIntegrationAdministrator_ViewsThePayloadFormats_AndDownloadsAnExample(Type controller, string action)
    {
        Assert.Null(RunInTenant(controller, action, ["IntegrationAdministrator"], isPrime: false).Result);
        Assert.Null(RunInTenant(controller, action, ["IntegrationAdministrator"], isPrime: true).Result);
        Assert.Null(RunInTenant(controller, action, ["PlatformAdministrator"], isPrime: false).Result);
        Assert.Equal(403, Assert.IsType<ViewResult>(RunInTenant(controller, action, ["Employee"], isPrime: false).Result).StatusCode);
    }

    [Fact]
    public async Task ARefusal_CarriesTheApisReason_ToTheExceptionAndThePage()
    {
        var handler = new StubHttpHandler(HttpStatusCode.Forbidden, """{"title":"Your account is not linked to an employee (no employee_id claim).","status":403}""", "application/problem+json");
        var bare = new StubHttpHandler(HttpStatusCode.Forbidden, """{"title":"Forbidden","status":403}""", "application/problem+json");

        var denied = await Assert.ThrowsAsync<ApiAccessDeniedException>(() => new ApprovalsApiClient(handler.CreateClient()).ListPendingAsync(null, null, null, null, null, 1, 20, CancellationToken.None));
        var plain = await Assert.ThrowsAsync<ApiAccessDeniedException>(() => new ApprovalsApiClient(bare.CreateClient()).ListPendingAsync(null, null, null, null, null, 1, 20, CancellationToken.None));

        Assert.Equal("robeem.Api refused the request (403): Your account is not linked to an employee (no employee_id claim).", denied.Message);
        Assert.Null(plain.Reason);
        var context = new ExceptionContext(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), []) { Exception = denied };
        new ApiAccessDeniedFilter().OnException(context);
        Assert.Contains("not linked to an employee", (string)Assert.IsType<ViewResult>(context.Result).ViewData[AccessDeniedPage.ReasonKey]!);
    }

    private static ActionExecutingContext Run(Type controller, string action, int? employeeId, string[] roles) =>
        Run(controller, action, new ActingAs(employeeId, "x", roles));

    private static ActionExecutingContext Run(Type controller, string action, ActingAs? choice, ActingAs? standIn = null)
    {
        // What MVC collects as endpoint metadata: the controller's attributes, then the action's.
        var method = controller.GetMethod(action)!;
        var descriptor = new ActionDescriptor
        {
            EndpointMetadata = [.. controller.GetCustomAttributes(inherit: true), .. method.GetCustomAttributes(inherit: true)],
        };
        var route = new RouteData();
        route.Values["controller"] = controller.Name[..^"Controller".Length];
        var context = new ActionExecutingContext(new ActionContext(new DefaultHttpContext(), route, descriptor), [], new Dictionary<string, object?>(), controller);

        RunFilter(new FakeActor(choice, standIn), context, controller);
        return context;
    }

    private static ActionExecutingContext RunInTenant(Type controller, string action, string[] roles, bool? isPrime)
    {
        // An action may have a GET and a POST overload: the metadata of every one named so, as the action's own.
        var methods = controller.GetMethods().Where(m => m.Name == action).ToList();
        Assert.NotEmpty(methods);
        var descriptor = new ActionDescriptor
        {
            EndpointMetadata = [.. controller.GetCustomAttributes(inherit: true), .. methods.SelectMany(m => m.GetCustomAttributes(inherit: true))],
        };
        var route = new RouteData();
        route.Values["controller"] = controller.Name[..^"Controller".Length];
        var context = new ActionExecutingContext(new ActionContext(new DefaultHttpContext(), route, descriptor), [], new Dictionary<string, object?>(), controller);

        new ActorAccessFilter(new FakeActor(new ActingAs(1, "x", roles)), new FakeTenant(isPrime))
            .OnActionExecutionAsync(context, () => Task.FromResult(new ActionExecutedContext(context, [], controller))).GetAwaiter().GetResult();
        return context;
    }

    private static void RunFilter(ICurrentActor actor, ActionExecutingContext context, Type controller) =>
        new ActorAccessFilter(actor).OnActionExecutionAsync(context, () => Task.FromResult(new ActionExecutedContext(context, [], controller))).GetAwaiter().GetResult();

    private sealed class FakeTenant : robeem.Web.Tenancy.ICurrentTenantInfo
    {
        public FakeTenant(bool? isPrime)
        {
            IsPrime = isPrime;
        }

        public bool? IsPrime { get; }

        public Task ResolveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeActor : ICurrentActor
    {
        public FakeActor(ActingAs? choice, ActingAs? standIn = null)
        {
            Choice = choice;
            Effective = choice ?? standIn;
        }

        public bool HasChoice => Choice is not null;

        public ActingAs? Choice { get; }

        public ActingAs? Effective { get; }

        public bool IsInRole(string role) => Effective is null || Effective.Roles.Contains(role);

        public bool IsInAnyRole(params string[] roles) => roles.Any(IsInRole);
    }

    private sealed class StubStandIn : IStandInApiClient
    {
        public StandInActor? Answer { get; set; }

        public bool Fails { get; set; }

        public int Calls { get; private set; }

        public Task<StandInActor?> GetAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Fails ? throw new HttpRequestException("down") : Task.FromResult(Answer);
        }
    }
}
