using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Tests.Api;

/// <summary>The compensation report and pay cycle endpoints: payroll administrators only, the decider is the signed-in user, and how a refusal is answered.</summary>
public sealed class CompensationEndpointTests : IAsyncDisposable
{
    private static readonly string Payroll = TestApi.As(7, AttendanceRoles.PayrollAdministrator);

    private readonly FakeCompensation _compensation = new();
    private readonly FakeCycles _cycles = new();
    private readonly TestApi _api;

    public CompensationEndpointTests()
    {
        _api = TestApi.Start(services =>
        {
            services.AddSingleton<IPayrollCompensationService>(_compensation);
            services.AddSingleton<IPayCycleService>(_cycles);
        });
    }

    public static IEnumerable<object[]> Endpoints() =>
    [
        ["GET", "api/attendance/payroll/compensation", null!],
        ["GET", "api/attendance/payroll/compensation/all", null!],
        ["GET", "api/attendance/payroll/compensation/5", null!],
        ["POST", "api/attendance/payroll/compensation/decisions", """{"employeeId":5,"periodStart":"2026-10-01","lines":[]}"""],
        ["POST", "api/attendance/payroll/compensation/close", """{"employeeId":5,"periodStart":"2026-10-01"}"""],
        ["POST", "api/attendance/payroll/compensation/close-all", """{"mode":"Latest"}"""],
        ["POST", "api/attendance/payroll/compensation/reopen", """{"employeeId":5,"periodStart":"2026-10-01","reason":"late fix"}"""],
        ["GET", "api/attendance/pay-cycles/default", null!],
        ["PUT", "api/attendance/pay-cycles/default", """{"frequency":"Monthly","startDay":26}"""],
        ["GET", "api/attendance/pay-cycles", null!],
        ["GET", "api/attendance/pay-cycles/employees/3", null!],
        ["PUT", "api/attendance/pay-cycles/employees/3", """{"frequency":"Weekly","anchorDate":"2026-04-01"}"""],
        ["DELETE", "api/attendance/pay-cycles/employees/3", null!],
    ];

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task EveryEndpoint_IsForPayrollAdministratorsOnly(string method, string url, string? body)
    {
        foreach (var role in new[] { AttendanceRoles.Employee, AttendanceRoles.Manager, AttendanceRoles.HRAdministrator, AttendanceRoles.IntegrationAdministrator })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(method, url, TestApi.As(7, role), body)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(method, url, null, body)).StatusCode);
        Assert.Empty(_compensation.Calls);
        Assert.Empty(_cycles.Calls);

        Assert.NotEqual(HttpStatusCode.Forbidden, (await Send(method, url, Payroll, body)).StatusCode);
    }

    [Fact]
    public async Task TheDecider_IsTheSignedInUser_NeverAFieldOfTheRequest()
    {
        var body = """{"employeeId":5,"periodStart":"2026-10-01","decidedById":999,"lines":[{"date":"2026-10-05","lineKind":"Overtime","decision":"Ignore"}]}""";

        var decided = await Send("POST", "api/attendance/payroll/compensation/decisions", Payroll, body);
        var noIdentity = await Send("POST", "api/attendance/payroll/compensation/decisions", TestApi.As(null, AttendanceRoles.PayrollAdministrator), body);

        Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
        Assert.Equal(3, (await decided.Content.ReadFromJsonAsync<CompensationDecidedResponse>())!.Decided);
        Assert.Equal(HttpStatusCode.Forbidden, noIdentity.StatusCode);
        var (request, by) = Assert.Single(_compensation.Decisions);
        Assert.Equal((7, 5, "Ignore"), (by, request.EmployeeId, request.Lines.Single().Decision));
    }

    [Fact]
    public async Task ARefusedDecision_Answers400KeyedByTheLine_AndAnUnknownEmployee404()
    {
        _compensation.Result = SaveResult.Invalid("Lines[0]", "Overtime is not allowed.");
        var refused = await Send("POST", "api/attendance/payroll/compensation/decisions", Payroll, """{"employeeId":5,"periodStart":"2026-10-01","lines":[]}""");

        _compensation.Result = SaveResult.Missing();
        var missing = await Send("POST", "api/attendance/payroll/compensation/decisions", Payroll, """{"employeeId":5,"periodStart":"2026-10-01","lines":[]}""");

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("Lines[0]", (await refused.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData("close", """{"employeeId":5,"periodStart":"2026-10-01"}""")]
    [InlineData("reopen", """{"employeeId":5,"periodStart":"2026-10-01","reason":"late fix"}""")]
    public async Task CloseAndReopen_Answer204_404_Or400(string action, string body)
    {
        var url = $"api/attendance/payroll/compensation/{action}";

        _compensation.Result = SaveResult.Success(1);
        Assert.Equal(HttpStatusCode.NoContent, (await Send("POST", url, Payroll, body)).StatusCode);

        _compensation.Result = SaveResult.Missing();
        Assert.Equal(HttpStatusCode.NotFound, (await Send("POST", url, Payroll, body)).StatusCode);

        _compensation.Result = SaveResult.Invalid("PeriodStart", "2 day(s) of the period are not ready for payroll.");
        var refused = await Send("POST", url, Payroll, body);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("not ready for payroll", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CloseAll_PassesTheOverviewsSearchModeAndDateOn_AndAnswersTheCounts()
    {
        var response = await Send("POST", "api/attendance/payroll/compensation/close-all", Payroll, """{"search":"ada","mode":"Containing","date":"2026-10-08"}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<CompensationCloseAllResult>())!;
        Assert.Equal((4, 1), (result.Closed, result.NotReady));
        Assert.Equal([("ada", "Containing", (DateTime?)new DateTime(2026, 10, 8))], _compensation.CloseAlls);
    }

    [Fact]
    public async Task TheOverview_PassesTheSearchTheModeTheDateAndThePageOn()
    {
        await Send("GET", "api/attendance/payroll/compensation?search=ada&mode=Containing&date=2026-10-08&page=3&pageSize=50", Payroll, null);
        await Send("GET", "api/attendance/payroll/compensation?page=-4", Payroll, null);

        Assert.Equal([("ada", "Containing", (DateTime?)new DateTime(2026, 10, 8), 3, 50), (null, null, null, 1, 0)], _compensation.Lists);
    }

    [Fact]
    public async Task TheDefaultCycle_IsSavedAndRead_AnEmployeesOwnIsAnswered404WhenThereIsNone_AndARefusalIs400()
    {
        _cycles.Result = SaveResult.Invalid("StartDay", "The month can start on day 1 to 28.");
        var refused = await Send("PUT", "api/attendance/pay-cycles/default", Payroll, """{"frequency":"Monthly","startDay":31}""");
        _cycles.Result = SaveResult.Success(1);
        var saved = await Send("PUT", "api/attendance/pay-cycles/employees/3", Payroll, """{"frequency":"Weekly","anchorDate":"2026-04-01"}""");
        var none = await Send("GET", "api/attendance/pay-cycles/employees/4", Payroll, null);
        var own = await Send("GET", "api/attendance/pay-cycles/employees/3", Payroll, null);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        Assert.Equal("Weekly", (await own.Content.ReadFromJsonAsync<PayCycleInput>())!.Frequency);
    }

    private Task<HttpResponseMessage> Send(string method, string url, string? user, string? body)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (user is not null)
        {
            request.Headers.Add("X-Test-User", user);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return _api.Client.SendAsync(request);
    }

    private sealed class FakeCompensation : IPayrollCompensationService
    {
        public List<string> Calls { get; } = [];

        public List<(CompensationDecisionRequest Request, int DecidedBy)> Decisions { get; } = [];

        public List<(string? Search, string? Mode, DateTime? Date, int Page, int PageSize)> Lists { get; } = [];

        public List<(string? Search, string? Mode, DateTime? Date)> CloseAlls { get; } = [];

        public SaveResult Result { get; set; } = SaveResult.Success(3);

        public Task<PagedResult<CompensationOverviewItem>> ListAsync(string? search, string? mode, DateTime? date, int page, int pageSize, CancellationToken cancellationToken)
        {
            Calls.Add("list");
            Lists.Add((search, mode, date, page, pageSize));
            return Task.FromResult(new PagedResult<CompensationOverviewItem>());
        }

        public Task<IReadOnlyList<CompensationOverviewItem>> ListAllAsync(string? search, string? mode, DateTime? date, CancellationToken cancellationToken)
        {
            Calls.Add("all");
            return Task.FromResult<IReadOnlyList<CompensationOverviewItem>>([]);
        }

        public Task<CompensationReport?> GetReportAsync(int employeeId, string? mode, DateTime? date, CancellationToken cancellationToken)
        {
            Calls.Add("report");
            return Task.FromResult<CompensationReport?>(new CompensationReport { EmployeeId = employeeId });
        }

        public Task<SaveResult> DecideAsync(CompensationDecisionRequest request, int decidedById, CancellationToken cancellationToken)
        {
            Calls.Add("decide");
            Decisions.Add((request, decidedById));
            return Task.FromResult(Result);
        }

        public Task<SaveResult> ClosePeriodAsync(CompensationCloseRequest request, CancellationToken cancellationToken)
        {
            Calls.Add("close");
            return Task.FromResult(Result);
        }

        public Task<CompensationCloseAllResult> CloseAllAsync(string? search, string? mode, DateTime? date, CancellationToken cancellationToken)
        {
            Calls.Add("close-all");
            CloseAlls.Add((search, mode, date));
            return Task.FromResult(new CompensationCloseAllResult { Closed = 4, NotReady = 1 });
        }

        public Task<SaveResult> ReopenPeriodAsync(CompensationReopenRequest request, CancellationToken cancellationToken)
        {
            Calls.Add("reopen");
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeCycles : IPayCycleService
    {
        public List<string> Calls { get; } = [];

        public SaveResult Result { get; set; } = SaveResult.Success(1);

        public Task<PayCycleInput?> GetDefaultAsync(CancellationToken cancellationToken) => Task.FromResult<PayCycleInput?>(null);

        public Task<PayCycleItem> DescribeDefaultAsync(CancellationToken cancellationToken)
        {
            Calls.Add("default");
            return Task.FromResult(new PayCycleItem());
        }

        public Task<SaveResult> SaveDefaultAsync(PayCycleInput input, CancellationToken cancellationToken)
        {
            Calls.Add("save-default");
            return Task.FromResult(Result);
        }

        public Task<PagedResult<EmployeePayCycleItem>> ListEmployeesAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
        {
            Calls.Add("list");
            return Task.FromResult(new PagedResult<EmployeePayCycleItem>());
        }

        public Task<PayCycleInput?> GetEmployeeAsync(int employeeId, CancellationToken cancellationToken)
        {
            Calls.Add("get");
            return Task.FromResult<PayCycleInput?>(employeeId == 3 ? new PayCycleInput { Frequency = "Weekly" } : null);
        }

        public Task<SaveResult> SaveEmployeeAsync(int employeeId, PayCycleInput input, CancellationToken cancellationToken)
        {
            Calls.Add("save");
            return Task.FromResult(Result);
        }

        public Task<SaveResult> RemoveEmployeeAsync(int employeeId, CancellationToken cancellationToken)
        {
            Calls.Add("remove");
            return Task.FromResult(Result);
        }
    }
}
