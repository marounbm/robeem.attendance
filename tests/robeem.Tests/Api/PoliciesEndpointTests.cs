using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.Contracts.HR.Employees;

namespace robeem.Tests.Api;

/// <summary>The policy endpoints (screens phase 4): roles, status codes, and how a refusal is answered.</summary>
public sealed class PoliciesEndpointTests : IAsyncDisposable
{
    private const string Body = """{"title":"Default","correctionApprovalWorkflow":"Manager","unlabeledPunchInterpretationMode":"Off","duplicateTimeWindowSeconds":120}""";

    private static readonly string Hr = TestApi.As(1, AttendanceRoles.HRAdministrator);

    private readonly FakePolicies _policies = new();
    private readonly TestApi _api;

    public PoliciesEndpointTests()
    {
        _api = TestApi.Start(services => services.AddSingleton<IAttendancePolicyService>(_policies));
    }

    public static IEnumerable<object[]> Endpoints() =>
    [
        ["GET", "api/attendance/policies", null!],
        ["GET", "api/attendance/policies/schedule-options", null!],
        ["GET", "api/attendance/policies/1/input", null!],
        ["POST", "api/attendance/policies", Body],
        ["PUT", "api/attendance/policies/1", Body],
        ["DELETE", "api/attendance/policies/1", null!],
    ];

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public void TheAuditActionCatalog_ListsEveryActionTheApplicationRecords()
    {
        var recorded = typeof(AttendanceAuditActions).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (string)f.GetRawConstantValue()!);

        Assert.Equal(recorded.Order(StringComparer.Ordinal), AuditActionCatalog.All.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task EveryEndpoint_IsForHrAdministratorsOnly(string method, string url, string? body)
    {
        foreach (var role in new[] { AttendanceRoles.Employee, AttendanceRoles.Manager, AttendanceRoles.PayrollAdministrator, AttendanceRoles.IntegrationAdministrator })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(method, url, TestApi.As(7, role), body)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(method, url, null, body)).StatusCode);
        Assert.Empty(_policies.Calls);

        Assert.NotEqual(HttpStatusCode.Forbidden, (await Send(method, url, Hr, body)).StatusCode);
        Assert.NotEmpty(_policies.Calls);
    }

    [Fact]
    public async Task Create_Answers201WithTheId_AndARefusalAs400KeyedByTheInputsProperties()
    {
        _policies.Result = SaveResult.Success(9);
        var created = await Send("POST", "api/attendance/policies", Hr, Body);

        _policies.Result = SaveResult.Invalid("TimeScheduleId", "That schedule has a policy already: change it instead.");
        var refused = await Send("POST", "api/attendance/policies", Hr, Body);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(9, (await created.Content.ReadFromJsonAsync<CreatedResponse>())!.Id);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("TimeScheduleId", (await refused.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys);
    }

    [Fact]
    public async Task AnOutOfRangeSetting_NeverReachesTheService()
    {
        var response = await Send("POST", "api/attendance/policies", Hr, """{"title":"x","correctionApprovalWorkflow":"Manager","unlabeledPunchInterpretationMode":"Off","roundingRuleMinutes":500,"duplicateTimeWindowSeconds":120}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("RoundingRuleMinutes", (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys);
        Assert.Empty(_policies.Calls);
    }

    [Fact]
    public async Task UpdateAndDelete_Answer204_404_Or400()
    {
        _policies.Result = SaveResult.Success(1);
        Assert.Equal(HttpStatusCode.NoContent, (await Send("PUT", "api/attendance/policies/1", Hr, Body)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send("DELETE", "api/attendance/policies/1", Hr, null)).StatusCode);

        _policies.Result = SaveResult.Missing();
        Assert.Equal(HttpStatusCode.NotFound, (await Send("PUT", "api/attendance/policies/9", Hr, Body)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send("DELETE", "api/attendance/policies/9", Hr, null)).StatusCode);

        _policies.Result = SaveResult.Invalid("id", "The tenant-wide default policy can't be deleted.");
        var refused = await Send("DELETE", "api/attendance/policies/1", Hr, null);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("can't be deleted", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AMissingPolicy_IsA404_ForItsInput()
    {
        _policies.Exists = false;

        Assert.Equal(HttpStatusCode.NotFound, (await Send("GET", "api/attendance/policies/9/input", Hr, null)).StatusCode);
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

    private sealed class FakePolicies : IAttendancePolicyService
    {
        public List<string> Calls { get; } = [];

        public SaveResult Result { get; set; } = SaveResult.Success(1);

        public bool Exists { get; set; } = true;

        public Task<IReadOnlyList<PolicyListItem>> ListAsync(CancellationToken cancellationToken) => Record<IReadOnlyList<PolicyListItem>>("list", []);

        public Task<PolicyInput?> GetInputAsync(int id, CancellationToken cancellationToken) => Record<PolicyInput?>("input", Exists ? new PolicyInput() : null);

        public Task<IReadOnlyList<ScheduleOption>> ListScheduleOptionsAsync(CancellationToken cancellationToken) => Record<IReadOnlyList<ScheduleOption>>("schedules", []);

        public Task<SaveResult> CreateAsync(PolicyInput input, CancellationToken cancellationToken) => Record("create", Result);

        public Task<SaveResult> UpdateAsync(int id, PolicyInput input, CancellationToken cancellationToken) => Record("update", Result);

        public Task<SaveResult> DeleteAsync(int id, CancellationToken cancellationToken) => Record("delete", Result);

        private Task<T> Record<T>(string call, T value)
        {
            Calls.Add(call);
            return Task.FromResult(value);
        }
    }
}
