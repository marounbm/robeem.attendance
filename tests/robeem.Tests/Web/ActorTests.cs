using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.FileProviders;
using robeem.Api.Security;
using robeem.Contracts.Security;
using robeem.Web.Actors;
using robeem.Web.ApiClients;
using robeem.Web.Controllers;
using robeem.Web.Navigation;

namespace robeem.Tests.Web;

/// <summary>The Development "act as" switcher on the Web side (screens phase 0).</summary>
public sealed class ActorTests
{
    [Theory]
    [InlineData("12;Employee,Manager", 12, new[] { "Employee", "Manager" })]
    [InlineData("-;IntegrationAdministrator", null, new[] { "IntegrationAdministrator" })]
    [InlineData("7;", 7, new string[0])]
    [InlineData("7; Employee , Employee ", 7, new[] { "Employee" })]
    public void TheProtocol_ReadsWhatItWrites(string value, int? employeeId, string[] roles)
    {
        Assert.True(ActorProtocol.TryParse(value, out var id, out var parsed));

        Assert.Equal(employeeId, id);
        Assert.Equal(roles, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12")]
    [InlineData("12;Employee;extra")]
    [InlineData("0;Employee")]
    [InlineData("-5;Employee")]
    [InlineData("abc;Employee")]
    [InlineData("1.5;Employee")]
    [InlineData("12;Employee,Root")]
    [InlineData("12;employee")]
    [InlineData("12;Employee\r\nX-Evil: 1")]
    public void AMalformedValue_IsRefused_NeverHalfRead(string? value)
    {
        Assert.False(ActorProtocol.TryParse(value, out var id, out var roles));
        Assert.Null(id);
        Assert.Empty(roles);
    }

    [Fact]
    public void Format_ThenParse_RoundTrips()
    {
        var text = ActorProtocol.Format(42, ["Manager", "Employee"]);

        Assert.Equal("42;Manager,Employee", text);
        Assert.True(ActorProtocol.TryParse(text, out var id, out var roles));
        Assert.Equal(42, id);
        Assert.Equal(["Manager", "Employee"], roles);
        Assert.Equal("-;", ActorProtocol.Format(null, []));
    }

    [Fact]
    public void TheWebsRoleNames_AreTheApisRoleNames()
    {
        Assert.Equal(AttendanceRoles.All.OrderBy(r => r), ActorProtocol.Roles.OrderBy(r => r));
    }

    [Fact]
    public void TheCookie_IsReadBack_WithItsDisplayName()
    {
        var request = Request("7;Employee,Manager", "Grace Hopper (M-1)");

        var acting = ActorCookie.Read(request)!;

        Assert.Equal((7, "Grace Hopper (M-1)"), (acting.EmployeeId, acting.Name));
        Assert.Equal(["Employee", "Manager"], acting.Roles);
        Assert.Equal("7;Employee,Manager", ActorCookie.ReadHeaderValue(request));
    }

    [Theory]
    [InlineData("7;Root")]
    [InlineData("garbage")]
    [InlineData("0;Employee")]
    public void ATamperedCookie_IsNeverForwarded(string cookie)
    {
        var request = Request(cookie, "Whoever");

        Assert.Null(ActorCookie.Read(request));
        Assert.Null(ActorCookie.ReadHeaderValue(request));
    }

    [Fact]
    public void TheDisplayName_IsCleaned_AndCapped()
    {
        var request = Request("7;Employee", "Bad\u0001Name" + new string('x', 500));

        var name = ActorCookie.Read(request)!.Name;

        Assert.DoesNotContain('\u0001', name);
        Assert.Equal(120, name.Length);
    }

    [Fact]
    public void WithoutADisplayName_TheCookieStillWorks()
    {
        var acting = ActorCookie.Read(Request("7;Employee", null))!;

        Assert.Equal("Employee 7", acting.Name);
    }

    [Theory]
    [InlineData("7;Employee,Manager", "7;Employee,Manager")]
    [InlineData("7;Root", null)]
    [InlineData(null, null)]
    public async Task TheHandler_ForwardsOnlyAValidChoice(string? cookie, string? expected)
    {
        var httpContext = new DefaultHttpContext();
        if (cookie is not null)
        {
            httpContext.Request.Headers.Cookie = $"{ActorCookie.Name}={Uri.EscapeDataString(cookie)}";
        }

        var stub = new StubHttpHandler(HttpStatusCode.OK);
        var handler = new ActorHeaderHandler(new HttpContextAccessor { HttpContext = httpContext }) { InnerHandler = stub };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") };

        await client.GetAsync("api/employees");

        var sent = stub.Requests.Single().Headers.TryGetValues(ActorProtocol.Header, out var values) ? values.Single() : null;
        Assert.Equal(expected, sent);
    }

    [Fact]
    public async Task TheHandler_ReplacesAHeaderTheCallerSet()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = $"{ActorCookie.Name}={Uri.EscapeDataString("7;Employee")}";
        var stub = new StubHttpHandler(HttpStatusCode.OK);
        var handler = new ActorHeaderHandler(new HttpContextAccessor { HttpContext = httpContext }) { InnerHandler = stub };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") };
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/employees");
        request.Headers.Add(ActorProtocol.Header, "1;HRAdministrator");

        await client.SendAsync(request);

        Assert.Equal("7;Employee", stub.Requests.Single().Headers.GetValues(ActorProtocol.Header).Single());
    }

    [Fact]
    public async Task TheActorsClient_ListsEmployees_AndReadsA404AsNoStandIn()
    {
        var ok = new ActorsApiClient(new StubHttpHandler(HttpStatusCode.OK, """[{"id":3,"name":"Grace Hopper (M-1)"}]""").CreateClient());
        var gone = new ActorsApiClient(new StubHttpHandler(HttpStatusCode.NotFound).CreateClient());

        Assert.Equal("Grace Hopper (M-1)", Assert.Single((await ok.ListAsync(CancellationToken.None))!).Name);
        Assert.Null(await gone.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ARefusal_BecomesAnAccessDeniedException_AndAMissingRecordANotFoundOne()
    {
        var forbidden = new EmployeesApiClient(new StubHttpHandler(HttpStatusCode.Forbidden).CreateClient());
        var unauthorized = new EmployeesApiClient(new StubHttpHandler(HttpStatusCode.Unauthorized).CreateClient());
        var missing = new EmployeesApiClient(new StubHttpHandler(HttpStatusCode.NotFound).CreateClient());

        await Assert.ThrowsAsync<ApiAccessDeniedException>(() => forbidden.ListAsync(null, false, CancellationToken.None));
        await Assert.ThrowsAsync<ApiAccessDeniedException>(() => unauthorized.ListSchedulesAsync(CancellationToken.None));
        await Assert.ThrowsAsync<ApiNotFoundException>(() => missing.ListSchedulesAsync(CancellationToken.None));
    }

    [Fact]
    public void TheFilter_ShowsTheAccessDeniedPage_ForARefusal()
    {
        var context = ExceptionContextFor(new ApiAccessDeniedException(HttpStatusCode.Forbidden));

        new ApiAccessDeniedFilter().OnException(context);

        var view = Assert.IsType<ViewResult>(context.Result);
        Assert.Equal(("AccessDenied", 403), (view.ViewName, view.StatusCode));
        Assert.True(context.ExceptionHandled);
    }

    [Fact]
    public void TheFilter_ShowsNotFound_ForAMissingRecord_AndLeavesOtherErrorsAlone()
    {
        var missing = ExceptionContextFor(new ApiNotFoundException());
        var other = ExceptionContextFor(new InvalidOperationException("boom"));

        new ApiAccessDeniedFilter().OnException(missing);
        new ApiAccessDeniedFilter().OnException(other);

        Assert.IsType<NotFoundResult>(missing.Result);
        Assert.Null(other.Result);
        Assert.False(other.ExceptionHandled);
    }

    [Fact]
    public void TheMenu_ShowsEverything_WhileNoChoiceWasMade_AndOnlyTheRolesScreensAfterwards()
    {
        var everyone = NavigationMenu.For(new FakeActor(null));
        var employee = NavigationMenu.For(new FakeActor(["Employee"]));
        var hr = NavigationMenu.For(new FakeActor(["HRAdministrator"]));

        Assert.Equal(NavigationMenu.Sections.Sum(s => s.Items.Count), everyone.Sum(s => s.Items.Count));
        Assert.DoesNotContain(employee.SelectMany(s => s.Items), i => i.Roles.Length > 0 && !i.Roles.Contains("Employee"));
        Assert.Contains(hr.SelectMany(s => s.Items), i => i.Controller == "Employees");
    }

    [Fact]
    public async Task Selecting_WritesTheCookie_ForAKnownEmployeeAndKnownRoles()
    {
        var controller = CreateController(development: true, out var http);

        var result = await controller.Select(3, ["Employee", "Manager"], null, null, CancellationToken.None);

        Assert.IsType<LocalRedirectResult>(result);
        var cookies = http.Response.Headers.SetCookie.ToString();
        Assert.Contains(Uri.EscapeDataString("3;Employee,Manager"), cookies);
        Assert.Contains(Uri.EscapeDataString("Grace Hopper (M-1)"), cookies);
    }

    [Fact]
    public async Task Selecting_APreset_OverridesTheTickedRoles()
    {
        var controller = CreateController(development: true, out var http);

        await controller.Select(3, ["HRAdministrator"], "Manager", null, CancellationToken.None);

        Assert.Contains(Uri.EscapeDataString("3;Employee,Manager"), http.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public async Task Selecting_RefusesAnUnknownEmployee_AnUnknownRole_AndAnythingOutsideDevelopment()
    {
        var dev = CreateController(development: true, out var devHttp);
        var prod = CreateController(development: false, out _);

        Assert.IsType<BadRequestObjectResult>(await dev.Select(999, ["Employee"], null, null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await dev.Select(3, ["Root"], null, null, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await prod.Select(3, ["Employee"], null, null, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await prod.Index(null, CancellationToken.None));
        Assert.Equal(0, devHttp.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public async Task ReturnUrl_MustBeLocal()
    {
        var controller = CreateController(development: true, out _);

        var local = Assert.IsType<LocalRedirectResult>(await controller.Select(3, ["Employee"], null, "/Employees?x=1", CancellationToken.None));
        var foreign = Assert.IsType<LocalRedirectResult>(await controller.Select(3, ["Employee"], null, "https://evil.test/", CancellationToken.None));

        Assert.Equal(("/Employees?x=1", "/"), (local.Url, foreign.Url));
    }

    private static HttpRequest Request(string cookie, string? displayName)
    {
        var context = new DefaultHttpContext();
        var cookies = $"{ActorCookie.Name}={Uri.EscapeDataString(cookie)}";
        if (displayName is not null)
        {
            cookies += $"; {ActorCookie.DisplayNameCookie}={Uri.EscapeDataString(displayName)}";
        }

        context.Request.Headers.Cookie = cookies;
        return context.Request;
    }

    private static ExceptionContext ExceptionContextFor(Exception exception) =>
        new(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), [])
        {
            Exception = exception,
        };

    private static ActorController CreateController(bool development, out DefaultHttpContext http)
    {
        http = new DefaultHttpContext();
        var controller = new ActorController(new FakeActors(), new FakeEnvironment(development ? "Development" : "Production"))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
        controller.Url = new UrlHelper(new ActionContext(http, new RouteData(), new ActionDescriptor()));
        return controller;
    }

    private sealed class FakeActors : IActorsApiClient
    {
        public Task<IReadOnlyList<ActorOption>?> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ActorOption>?>([new ActorOption { Id = 3, Name = "Grace Hopper (M-1)" }]);
    }

    private sealed class FakeActor : ICurrentActor
    {
        private readonly string[]? _roles;

        public FakeActor(string[]? roles)
        {
            _roles = roles;
        }

        public bool HasChoice => _roles is not null;

        public ActingAs? Choice => _roles is null ? null : new ActingAs(null, "x", _roles);

        public bool IsInRole(string role) => _roles is null || _roles.Contains(role);

        public bool IsInAnyRole(params string[] roles) => roles.Any(IsInRole);
    }

    private sealed class FakeEnvironment(string name) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "robeem.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
