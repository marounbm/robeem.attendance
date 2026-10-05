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

/// <summary>The pay period and payroll endpoints (screens phase 3): roles, status codes and how a refusal is answered.</summary>
public sealed class PayPeriodsEndpointTests : IAsyncDisposable
{
    private static readonly string Payroll = TestApi.As(1, AttendanceRoles.PayrollAdministrator);

    private readonly FakePeriods _periods = new();
    private readonly FakeExtract _extract = new();
    private readonly TestApi _api;

    public PayPeriodsEndpointTests()
    {
        _api = TestApi.Start(services =>
        {
            services.AddSingleton<IPayPeriodService>(_periods);
            services.AddSingleton<IPayrollAttendanceExtract>(_extract);
        });
    }

    public static IEnumerable<object[]> Endpoints() =>
    [
        ["GET", "api/attendance/pay-periods", null!],
        ["POST", "api/attendance/pay-periods", """{"startDate":"2026-10-01","endDate":"2026-10-31"}"""],
        ["POST", "api/attendance/pay-periods/1/close", null!],
        ["POST", "api/attendance/pay-periods/1/reopen", """{"reason":"late timesheet"}"""],
        ["GET", "api/attendance/payroll/approved?from=2026-10-01&to=2026-10-31", null!],
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
        Assert.Empty(_periods.Calls);
        Assert.Equal(0, _extract.Asked);

        Assert.NotEqual(HttpStatusCode.Forbidden, (await Send(method, url, Payroll, body)).StatusCode);
    }

    [Fact]
    public async Task Create_Answers201WithTheId_AndARefusalAs400KeyedByTheInputsProperties()
    {
        _periods.Result = SaveResult.Success(42);
        var created = await Send("POST", "api/attendance/pay-periods", Payroll, """{"startDate":"2026-10-01","endDate":"2026-10-31"}""");

        _periods.Result = SaveResult.Invalid("StartDate", "The period overlaps another one.");
        var refused = await Send("POST", "api/attendance/pay-periods", Payroll, """{"startDate":"2026-10-01","endDate":"2026-10-31"}""");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(42, (await created.Content.ReadFromJsonAsync<CreatedResponse>())!.Id);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("StartDate", (await refused.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys);
    }

    [Theory]
    [InlineData("close")]
    [InlineData("reopen")]
    public async Task CloseAndReopen_Answer204_404_Or400(string action)
    {
        var body = action == "reopen" ? """{"reason":"late timesheet"}""" : null;
        var url = $"api/attendance/pay-periods/1/{action}";

        _periods.Result = SaveResult.Success(1);
        Assert.Equal(HttpStatusCode.NoContent, (await Send("POST", url, Payroll, body)).StatusCode);

        _periods.Result = SaveResult.Missing();
        Assert.Equal(HttpStatusCode.NotFound, (await Send("POST", url, Payroll, body)).StatusCode);

        _periods.Result = SaveResult.Invalid("id", "The pay period is already closed.");
        var refused = await Send("POST", url, Payroll, body);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("already closed", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reopen_PassesTheReasonOn()
    {
        await Send("POST", "api/attendance/pay-periods/3/reopen", Payroll, """{"reason":"late timesheet"}""");

        Assert.Equal([("reopen", 3, "late timesheet")], _periods.Reopens);
    }

    [Fact]
    public async Task Payroll_ReadsTheApprovedDays_AndRefusesABackwardsRange()
    {
        var ok = await Send("GET", "api/attendance/payroll/approved?from=2026-10-01&to=2026-10-31", Payroll, null);
        var backwards = await Send("GET", "api/attendance/payroll/approved?from=2026-10-31&to=2026-10-01", Payroll, null);

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);
        Assert.Equal(1, _extract.Asked);
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

    private sealed class FakePeriods : IPayPeriodService
    {
        public List<string> Calls { get; } = [];

        public List<(string Kind, int Id, string? Reason)> Reopens { get; } = [];

        public SaveResult Result { get; set; } = SaveResult.Success(1);

        public Task<IReadOnlyList<PayPeriodItem>> ListAsync(CancellationToken cancellationToken)
        {
            Calls.Add("list");
            return Task.FromResult<IReadOnlyList<PayPeriodItem>>([]);
        }

        public Task<SaveResult> CreateAsync(PayPeriodInput input, CancellationToken cancellationToken) => Record("create");

        public Task<SaveResult> CloseAsync(int id, CancellationToken cancellationToken) => Record("close");

        public Task<SaveResult> ReopenAsync(int id, string? reason, CancellationToken cancellationToken)
        {
            Reopens.Add(("reopen", id, reason));
            return Record("reopen");
        }

        private Task<SaveResult> Record(string call)
        {
            Calls.Add(call);
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeExtract : IPayrollAttendanceExtract
    {
        public int Asked { get; private set; }

        public Task<IReadOnlyList<PayrollAttendanceDay>> GetApprovedRecordsAsync(DateTime from, DateTime to, CancellationToken cancellationToken)
        {
            Asked++;
            return Task.FromResult<IReadOnlyList<PayrollAttendanceDay>>([]);
        }
    }
}
