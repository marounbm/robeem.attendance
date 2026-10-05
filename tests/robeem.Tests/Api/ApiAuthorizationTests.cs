using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using robeem.Api.Controllers;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Security;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Tests.Api;

/// <summary>
/// The role checks of the Api (P12, study §16.5): real requests through the real controllers and
/// their [Authorize] attributes, on an in-memory server, with the application services faked.
/// The signed-in user is set by headers, standing in for the claims of a validated token.
/// </summary>
public sealed class ApiAuthorizationTests : IAsyncDisposable
{
    private readonly FakeCorrectionService _corrections = new();
    private readonly FakePayroll _payroll = new();
    private readonly IHost _host;
    private readonly HttpClient _client;

    public ApiAuthorizationTests()
    {
        _host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddControllers().AddApplicationPart(typeof(AttendanceCorrectionsController).Assembly);
                    services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("Test", null);
                    services.AddAuthorization();
                    services.AddHttpContextAccessor();
                    services.AddScoped<ICurrentUser, HttpCurrentUser>();
                    services.AddSingleton<IAttendanceCorrectionService>(_corrections);
                    services.AddSingleton<IPayrollAttendanceExtract>(_payroll);
                    services.AddSingleton<IIntegrationHealthService, FakeHealth>();
                    services.AddSingleton<IAttendanceAuditTrail, FakeAudit>();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                }))
            .Build();
        _host.Start();
        _client = _host.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task AnEmployee_CanNotCallTheDecisionEndpoint()
    {
        var response = await DecideAsync(As(7, AttendanceRoles.Employee));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_corrections.Decisions);
    }

    [Fact]
    public async Task AnEmployee_CanNotListTheApproversQueue()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "api/attendance/corrections/pending?awaiting=Manager", As(7, AttendanceRoles.Employee))).StatusCode);
    }

    [Fact]
    public async Task APayrollAdministrator_CanReadTheApprovedRecords_ButNotDecideACorrection()
    {
        var payroll = As(null, AttendanceRoles.PayrollAdministrator);

        var approved = await Send(HttpMethod.Get, "api/attendance/payroll/approved?from=2026-10-05&to=2026-10-11", payroll);
        var decision = await DecideAsync(payroll);

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal((new DateTime(2026, 10, 5), new DateTime(2026, 10, 11)), _payroll.LastRange);
        Assert.Equal(HttpStatusCode.Forbidden, decision.StatusCode);
        Assert.Empty(_corrections.Decisions);
    }

    [Theory]
    [InlineData(AttendanceRoles.Employee)]
    [InlineData(AttendanceRoles.Manager)]
    [InlineData(AttendanceRoles.HRAdministrator)]
    [InlineData(AttendanceRoles.IntegrationAdministrator)]
    public async Task AnyoneElse_CanNotReadThePayrollGate(string role)
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "api/attendance/payroll/approved?from=2026-10-05&to=2026-10-11", As(7, role))).StatusCode);
        Assert.Null(_payroll.LastRange);
    }

    [Fact]
    public async Task APayrollRequestWithAReversedPeriod_IsRefusedAs400()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Get, "api/attendance/payroll/approved?from=2026-10-11&to=2026-10-05", As(null, AttendanceRoles.PayrollAdministrator))).StatusCode);
    }

    [Fact]
    public async Task ANobody_IsNotAuthenticated()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await DecideAsync(null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "api/attendance/payroll/approved?from=2026-10-05&to=2026-10-11", null)).StatusCode);
    }

    [Fact]
    public async Task AManager_DecidesAsThemselves_AndTheBodyCanNotMakeThemHr()
    {
        var response = await DecideAsync(As(12, AttendanceRoles.Manager), approved: true, isHr: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((5, 12, true, false), Assert.Single(_corrections.Decisions));
    }

    [Fact]
    public async Task AnHrAdministrator_DecidesAsHr()
    {
        await DecideAsync(As(13, AttendanceRoles.HRAdministrator), approved: true, isHr: false);

        Assert.Equal((5, 13, true, true), Assert.Single(_corrections.Decisions));
    }

    [Fact]
    public async Task AUserWithBothRoles_SaysWhichTheyDecideAs()
    {
        var both = As(14, AttendanceRoles.Manager, AttendanceRoles.HRAdministrator);

        await DecideAsync(both, approved: true, isHr: false);
        await DecideAsync(both, approved: false, isHr: true);

        Assert.Equal([(5, 14, true, false), (5, 14, false, true)], _corrections.Decisions);
    }

    [Fact]
    public async Task AnApproverWithoutAnEmployeeIdentity_IsRefused()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await DecideAsync(As(null, AttendanceRoles.Manager))).StatusCode);
        Assert.Empty(_corrections.Decisions);
    }

    [Fact]
    public async Task AManager_SeesOnlyTheirOwnQueue()
    {
        var manager = As(12, AttendanceRoles.Manager);

        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "api/attendance/corrections/pending?awaiting=Manager", manager)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "api/attendance/corrections/pending?awaiting=HR", manager)).StatusCode);
    }

    [Fact]
    public async Task AnEmployee_ProposesAsThemselves_NotAsTheBodySays()
    {
        var response = await Send(HttpMethod.Post, "api/attendance/corrections", As(7, AttendanceRoles.Employee), new { exceptionId = 3, proposedValue = "2026-10-05T12:00:00", justification = "Reader missed it", requestedByEmployeeId = 999 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(7, Assert.Single(_corrections.Proposals));
    }

    [Fact]
    public async Task AManagerWhoIsNotAlsoAnEmployee_CanNotPropose_AndAnEmployeeWithoutAnIdentityCanNot_Either()
    {
        var body = new { exceptionId = 3, proposedValue = "2026-10-05T12:00:00", justification = "x" };

        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "api/attendance/corrections", As(12, AttendanceRoles.Manager), body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "api/attendance/corrections", As(null, AttendanceRoles.Employee), body)).StatusCode);
        Assert.Empty(_corrections.Proposals);
    }

    [Fact]
    public async Task TheIntegrationAndAuditEndpoints_AreForTheirRolesOnly()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, "api/attendance/integrations/1/health", As(1, AttendanceRoles.IntegrationAdministrator))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "api/attendance/integrations/1/health", As(1, AttendanceRoles.HRAdministrator))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "api/attendance/audit", As(1, AttendanceRoles.HRAdministrator))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "api/attendance/audit", As(1, AttendanceRoles.Manager))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "api/attendance/audit", As(1, AttendanceRoles.PayrollAdministrator))).StatusCode);
    }

    [Fact]
    public async Task TheHrScreensEndpoints_NeedTheHrRole()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "api/employees", As(7, AttendanceRoles.Employee))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "api/schedules", As(7, AttendanceRoles.Manager))).StatusCode);
    }

    [Fact]
    public void EveryActionOfTheApi_NamesItsRoles_ExceptTheTenantPicker()
    {
        var controllers = typeof(AttendanceCorrectionsController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract && t != typeof(TenantsController))
            .ToList();
        Assert.NotEmpty(controllers);

        // An action is covered when its class or the action itself carries [Authorize(Roles = ...)].
        var unprotected = controllers
            .SelectMany(c => c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
                .Select(m => (Controller: c, Action: m)))
            .Where(x => !x.Controller.GetCustomAttributes<AuthorizeAttribute>().Concat(x.Action.GetCustomAttributes<AuthorizeAttribute>()).Any(a => !string.IsNullOrEmpty(a.Roles)))
            .Select(x => $"{x.Controller.Name}.{x.Action.Name}");

        Assert.Empty(unprotected);
    }

    private static string As(int? employeeId, params string[] roles) =>
        $"{employeeId?.ToString() ?? "-"};{string.Join(",", roles)}";

    private Task<HttpResponseMessage> DecideAsync(string? user, bool approved = true, bool isHr = false) =>
        Send(HttpMethod.Post, "api/attendance/corrections/5/decision", user, new { approved, isHrDecision = isHr });

    private async Task<HttpResponseMessage> Send(HttpMethod method, string url, string? user, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (user is not null)
        {
            request.Headers.Add(HeaderAuthenticationHandler.Header, user);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }

    /// <summary>Signs in whoever the header names: <c>{employee id or -};{roles, comma-separated}</c>.</summary>
    private sealed class HeaderAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string Header = "X-Test-User";

        public HeaderAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(Header, out var value))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var parts = value.ToString().Split(';');
            var claims = new List<Claim> { new("sub", "test") };
            claims.AddRange(parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(role => new Claim(SecurityClaims.Role, role)));
            if (parts[0] != "-")
            {
                claims.Add(new Claim(SecurityClaims.EmployeeId, parts[0]));
            }

            var identity = new ClaimsIdentity(claims, "Test", "sub", SecurityClaims.Role);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }

    private sealed class FakeCorrectionService : IAttendanceCorrectionService
    {
        public List<(int CorrectionId, int DecidedBy, bool Approved, bool AsHr)> Decisions { get; } = [];

        public List<int> Proposals { get; } = [];

        public Task<CorrectionResult> ProposeCorrectionAsync(int exceptionId, DateTime proposedValue, string justification, int requestedByEmployeeId, CancellationToken cancellationToken)
        {
            Proposals.Add(requestedByEmployeeId);
            return Task.FromResult(CorrectionResult.Success(new AttendanceCorrection { Id = 1 }, AttendanceExceptionStatus.PendingCorrection));
        }

        public Task<CorrectionResult> DecideCorrectionAsync(int correctionId, int decidedByUserId, bool approved, bool isHrDecision, CancellationToken cancellationToken)
        {
            Decisions.Add((correctionId, decidedByUserId, approved, isHrDecision));
            return Task.FromResult(CorrectionResult.Success(new AttendanceCorrection { Id = correctionId, Status = AttendanceCorrectionStatus.Applied }, AttendanceExceptionStatus.Closed));
        }

        public Task<IReadOnlyList<CorrectionListItem>> ListPendingAsync(CorrectionApprover approver, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CorrectionListItem>>([]);
    }

    private sealed class FakePayroll : IPayrollAttendanceExtract
    {
        public (DateTime From, DateTime To)? LastRange { get; private set; }

        public Task<IReadOnlyList<PayrollAttendanceDay>> GetApprovedRecordsAsync(DateTime payPeriodStart, DateTime payPeriodEnd, CancellationToken cancellationToken)
        {
            LastRange = (payPeriodStart, payPeriodEnd);
            return Task.FromResult<IReadOnlyList<PayrollAttendanceDay>>([]);
        }
    }

    private sealed class FakeHealth : IIntegrationHealthService
    {
        public Task<IntegrationHealthReport?> GetIntegrationHealthAsync(int integrationId, CancellationToken cancellationToken) =>
            Task.FromResult<IntegrationHealthReport?>(null);
    }

    private sealed class FakeAudit : IAttendanceAuditTrail
    {
        public void Record(string action, string entityType, int? entityId, string? details = null, string? actor = null)
        {
        }

        public void RecordNew(string action, Entity entity, string? details = null)
        {
        }

        public Task FlushNewAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<AuditLogEntry>> ListAsync(string? action, string? entityType, int? entityId, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AuditLogEntry>>([]);
    }
}
