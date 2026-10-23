using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using robeem.Api.Controllers;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Common;
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
    private readonly FakeExceptions _exceptions = new();
    private readonly FakeReviews _reviews = new();
    private readonly TestApi _api;

    public ApiAuthorizationTests()
    {
        _api = TestApi.Start(services =>
        {
            services.AddSingleton<IAttendanceExceptionQuery>(_exceptions);
            services.AddSingleton<IAttendanceExceptionService>(_exceptions);
            services.AddSingleton<IAttendanceReviewService>(_reviews);
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
    public async Task TheQueue_IsAskedForTheAuthenticatedUser_NotForWhoeverTheCallerNames()
    {
        await Send(HttpMethod.Get, "api/attendance/corrections/pending?awaiting=Manager&approverEmployeeId=999", As(12, AttendanceRoles.Manager));
        await Send(HttpMethod.Get, "api/attendance/corrections/pending?awaiting=HR", As(13, AttendanceRoles.HRAdministrator));

        Assert.Equal([(CorrectionApprover.Manager, 12), (CorrectionApprover.HR, 13)], _corrections.Lists);
    }

    [Fact]
    public async Task HrWithoutAnEmployeeIdentity_CanLookAtTheHrQueues_ButNotDecide_NorLookAtAManagersQueue()
    {
        // The Development stand-in user (every role, no employee) is in this position until someone is chosen in "act as".
        var hr = As(null, AttendanceRoles.HRAdministrator);
        var standIn = As(null, AttendanceRoles.Employee, AttendanceRoles.Manager, AttendanceRoles.HRAdministrator);

        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "api/attendance/corrections/pending?awaiting=HR", hr)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "api/attendance/reviews/pending", hr)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "api/attendance/corrections/pending?awaiting=Manager", standIn)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DecideAsync(hr)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "api/attendance/reviews/5/decision", hr, new { approved = true })).StatusCode);

        Assert.Equal([(CorrectionApprover.HR, 0)], _corrections.Lists);
        Assert.Equal([(0, true)], _reviews.Lists);
        Assert.Empty(_corrections.Decisions);
        Assert.Empty(_reviews.Decisions);
    }

    [Fact]
    public async Task AnApproverWithoutAnEmployeeIdentity_CanNotListAQueue()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "api/attendance/corrections/pending?awaiting=Manager", As(null, AttendanceRoles.Manager))).StatusCode);
        Assert.Empty(_corrections.Lists);
    }

    [Fact]
    public async Task Exceptions_AreListedForTheUsersOwnRoles_NotForWhatTheCallerClaims()
    {
        await Send(HttpMethod.Get, "api/attendance/exceptions?employeeId=5", As(7, AttendanceRoles.Employee));
        await Send(HttpMethod.Get, "api/attendance/exceptions", As(12, AttendanceRoles.Manager, AttendanceRoles.Employee));
        await Send(HttpMethod.Get, "api/attendance/exceptions?status=closed&take=20", As(13, AttendanceRoles.HRAdministrator));

        Assert.Equal(
            [
                (new ExceptionViewer(7, true, false, false), 5, ExceptionStatusFilter.Active),
                (new ExceptionViewer(12, true, true, false), null, ExceptionStatusFilter.Active),
                (new ExceptionViewer(13, false, false, true), null, ExceptionStatusFilter.Closed),
            ],
            _exceptions.Calls.Select(c => (c.Viewer, c.Filter.EmployeeId, c.Filter.Status)));
        Assert.Equal(20, _exceptions.Calls[2].Filter.Take);
    }

    [Fact]
    public async Task Exceptions_AreListedForHrWithoutAnEmployeeLink_ButNotForAnEmployeeOrManagerWithout()
    {
        // The Development stand-in user (every role, no employee) is HR for this purpose; its other roles add no scope.
        var hr = await Send(HttpMethod.Get, "api/attendance/exceptions", As(null, AttendanceRoles.HRAdministrator));
        var standIn = await Send(HttpMethod.Get, "api/attendance/exceptions", As(null, AttendanceRoles.Employee, AttendanceRoles.Manager, AttendanceRoles.HRAdministrator));
        var manager = await Send(HttpMethod.Get, "api/attendance/exceptions", As(null, AttendanceRoles.Manager));

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Forbidden), (hr.StatusCode, standIn.StatusCode, manager.StatusCode));
        Assert.Equal(
            [new ExceptionViewer(0, false, false, true), new ExceptionViewer(0, false, false, true)],
            _exceptions.Calls.Select(c => c.Viewer));
    }

    [Fact]
    public async Task Exceptions_AreNotForPayrollOrIntegrationAdministrators_NorForSomeoneWithNoEmployeeIdentity_NorABadStatus()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "api/attendance/exceptions", As(7, AttendanceRoles.PayrollAdministrator))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "api/attendance/exceptions", As(7, AttendanceRoles.IntegrationAdministrator))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "api/attendance/exceptions", As(null, AttendanceRoles.Employee))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Get, "api/attendance/exceptions?status=nonsense", As(7, AttendanceRoles.Employee))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "api/attendance/exceptions", null)).StatusCode);
        Assert.Empty(_exceptions.Calls);
    }

    [Fact]
    public async Task TheReviewQueue_IsAskedForTheAuthenticatedUser_AndHrSeesEveryone()
    {
        await Send(HttpMethod.Get, "api/attendance/reviews/pending", As(12, AttendanceRoles.Manager));
        await Send(HttpMethod.Get, "api/attendance/reviews/pending", As(13, AttendanceRoles.HRAdministrator));

        Assert.Equal([(12, false), (13, true)], _reviews.Lists);
    }

    [Fact]
    public async Task AcceptingAnException_IsForApproversOnly_WithAnEmployeeIdentity()
    {
        var body = new { reason = "Covered the phones" };

        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "api/attendance/exceptions/5/accept", As(7, AttendanceRoles.Employee), body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "api/attendance/exceptions/5/accept", As(7, AttendanceRoles.PayrollAdministrator), body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "api/attendance/exceptions/5/accept", As(null, AttendanceRoles.Manager), body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Post, "api/attendance/exceptions/5/accept", null, body)).StatusCode);
        Assert.Empty(_exceptions.Accepted);
    }

    [Fact]
    public async Task AnException_IsAcceptedAsTheUser_InTheirRole_WithTheReason()
    {
        var first = await Send(HttpMethod.Post, "api/attendance/exceptions/5/accept", As(12, AttendanceRoles.Manager), new { reason = "Covered the phones", isHrDecision = true });
        await Send(HttpMethod.Post, "api/attendance/exceptions/6/accept", As(13, AttendanceRoles.HRAdministrator), new { reason = "Agreed with the employee" });
        await Send(HttpMethod.Post, "api/attendance/exceptions/7/accept", As(14, AttendanceRoles.Manager, AttendanceRoles.HRAdministrator), new { reason = "Policy exception", isHrDecision = true });

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(
            [(5, 12, false, "Covered the phones"), (6, 13, true, "Agreed with the employee"), (7, 14, true, "Policy exception")],
            _exceptions.Accepted);
    }

    [Fact]
    public async Task Reviews_AreForApproversOnly_WithAnEmployeeIdentity()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "api/attendance/reviews/pending", As(7, AttendanceRoles.Employee))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "api/attendance/reviews/pending", As(7, AttendanceRoles.PayrollAdministrator))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "api/attendance/reviews/5/decision", As(7, AttendanceRoles.Employee), new { approved = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "api/attendance/reviews/pending", As(null, AttendanceRoles.Manager))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "api/attendance/reviews/5/decision", As(null, AttendanceRoles.Manager), new { approved = true })).StatusCode);
        Assert.Empty(_reviews.Decisions);
        Assert.Empty(_reviews.Lists);
    }

    [Fact]
    public async Task AReviewDecision_IsMadeAsTheUser_InTheirRole_WithTheReason()
    {
        await Send(HttpMethod.Post, "api/attendance/reviews/5/decision", As(12, AttendanceRoles.Manager), new { approved = false, reason = "Not authorised.", isHrDecision = true });
        await Send(HttpMethod.Post, "api/attendance/reviews/6/decision", As(13, AttendanceRoles.HRAdministrator), new { approved = true });
        await Send(HttpMethod.Post, "api/attendance/reviews/7/decision", As(14, AttendanceRoles.Manager, AttendanceRoles.HRAdministrator), new { approved = true, isHrDecision = true });

        Assert.Equal(
            [(5, 12, false, false, "Not authorised."), (6, 13, true, true, null), (7, 14, true, true, null)],
            _reviews.Decisions);
    }

    [Fact]
    public async Task AnEmployee_ProposesAsThemselves_NotAsTheBodySays()
    {
        var response = await Send(HttpMethod.Post, "api/attendance/corrections", As(7, AttendanceRoles.Employee), new { exceptionId = 3, proposedValue = "2026-10-05T12:00:00", justification = "Reader missed it", requestedByEmployeeId = 999 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(7, Assert.Single(_corrections.Proposals));
    }

    [Fact]
    public async Task AnEmployee_ProposesAChangeOfTypeAsThemselves_WhenTheRequestNamesAType_AndATimeOtherwise()
    {
        var change = await Send(HttpMethod.Post, "api/attendance/corrections", As(7, AttendanceRoles.Employee), new { exceptionId = 3, newEventType = "CheckIn", justification = "I came back from lunch", requestedByEmployeeId = 999 });
        var missing = await Send(HttpMethod.Post, "api/attendance/corrections", As(7, AttendanceRoles.Employee), new { exceptionId = 4, proposedValue = "2026-10-05T12:00:00", justification = "Reader missed it" });
        var manager = await Send(HttpMethod.Post, "api/attendance/corrections", As(12, AttendanceRoles.Manager), new { exceptionId = 3, newEventType = "CheckIn", justification = "x" });

        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.Forbidden), (change.StatusCode, missing.StatusCode, manager.StatusCode));
        Assert.Equal([(3, "CheckIn", 7)], _corrections.TypeChanges);
        Assert.Equal([7], _corrections.Proposals);
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
    public async Task TheAuditEndpoint_IsForHrOnly()
    {
        // The integration endpoints, health included, are in IntegrationsEndpointTests.
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
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract && t != typeof(TenantsController) && t != typeof(DevelopmentActorsController))
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

        public List<(int ExceptionId, string NewEventType, int RequestedBy)> TypeChanges { get; } = [];

        public List<(CorrectionApprover Approver, int ApproverId)> Lists { get; } = [];

        public Task<CorrectionResult> ProposeTypeChangeAsync(int exceptionId, string newEventType, string justification, int requestedByEmployeeId, CancellationToken cancellationToken)
        {
            TypeChanges.Add((exceptionId, newEventType, requestedByEmployeeId));
            return Task.FromResult(CorrectionResult.Success(new AttendanceCorrection { Id = 2 }, AttendanceExceptionStatus.PendingCorrection));
        }

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

        public Task<IReadOnlyList<CorrectionListItem>> ListPendingAsync(CorrectionApprover approver, int approverEmployeeId, CancellationToken cancellationToken)
        {
            Lists.Add((approver, approverEmployeeId));
            return Task.FromResult<IReadOnlyList<CorrectionListItem>>([]);
        }
    }

    private sealed class FakeExceptions : IAttendanceExceptionQuery, IAttendanceExceptionService
    {
        public List<(ExceptionFilter Filter, ExceptionViewer Viewer)> Calls { get; } = [];

        public List<(int ExceptionId, int AcceptedBy, bool AsHr, string? Reason)> Accepted { get; } = [];

        public Task<SaveResult> AcceptAsync(int exceptionId, int acceptedByEmployeeId, bool asHr, string? reason, CancellationToken cancellationToken)
        {
            Accepted.Add((exceptionId, acceptedByEmployeeId, asHr, reason));
            return Task.FromResult(SaveResult.Success(exceptionId));
        }

        public Task<IReadOnlyList<ExceptionListItem>> ListAsync(ExceptionFilter filter, ExceptionViewer viewer, CancellationToken cancellationToken)
        {
            Calls.Add((filter, viewer));
            return Task.FromResult<IReadOnlyList<ExceptionListItem>>([]);
        }
    }

    private sealed class FakeReviews : IAttendanceReviewService
    {
        public List<(int RecordId, int DecidedBy, bool Approved, bool AsHr, string? Reason)> Decisions { get; } = [];

        public List<(int ApproverId, bool IsHr)> Lists { get; } = [];

        public Task<ReviewResult> DecideAsync(int recordId, int decidedByEmployeeId, bool asHr, bool approved, string? reason, CancellationToken cancellationToken)
        {
            Decisions.Add((recordId, decidedByEmployeeId, approved, asHr, reason));
            return Task.FromResult(ReviewResult.Success(new AttendanceReviewDecision { Id = 1, Outcome = approved ? AttendanceReviewOutcome.Approved : AttendanceReviewOutcome.Rejected }, AttendanceRecordStatus.Approved));
        }

        public Task<IReadOnlyList<ReviewListItem>> ListPendingAsync(int approverEmployeeId, bool isHr, CancellationToken cancellationToken)
        {
            Lists.Add((approverEmployeeId, isHr));
            return Task.FromResult<IReadOnlyList<ReviewListItem>>([]);
        }
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
