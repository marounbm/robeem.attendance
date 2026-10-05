using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using robeem.Api.Controllers;
using robeem.Api.Security;
using robeem.Application.Attendance;
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
    private readonly TestApi _api;

    public ApiAuthorizationTests()
    {
        _api = TestApi.Start(services =>
        {
            services.AddSingleton<IAttendanceCorrectionService>(_corrections);
            services.AddSingleton<IPayrollAttendanceExtract>(_payroll);
            services.AddSingleton<IIntegrationHealthService, FakeHealth>();
            services.AddSingleton<IAttendanceAuditTrail, FakeAudit>();
        });
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

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

    private static string As(int? employeeId, params string[] roles) => TestApi.As(employeeId, roles);

    private Task<HttpResponseMessage> DecideAsync(string? user, bool approved = true, bool isHr = false) =>
        Send(HttpMethod.Post, "api/attendance/corrections/5/decision", user, new { approved, isHrDecision = isHr });

    private Task<HttpResponseMessage> Send(HttpMethod method, string url, string? user, object? body = null) =>
        _api.Send(method, url, user, body);

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
