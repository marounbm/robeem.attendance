using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using robeem.Api.Security;
using robeem.Application.Common;
using robeem.Application.HR.Schedules;
using robeem.Contracts.Common;
using robeem.Contracts.HR.Schedules;

namespace robeem.Tests.Api;

/// <summary>The time schedule endpoints: HR only, and how each outcome of the service is answered.</summary>
public sealed class TimeSchedulesEndpointTests : IAsyncDisposable
{
    private static readonly string Hr = TestApi.As(1, AttendanceRoles.HRAdministrator);

    private readonly FakeSchedules _schedules = new();
    private readonly TestApi _api;

    public TimeSchedulesEndpointTests()
    {
        _api = TestApi.Start(services => services.AddSingleton<ITimeScheduleService>(_schedules));
    }

    public static IEnumerable<object[]> Endpoints() =>
    [
        ["GET", "api/time-schedules", null!],
        ["GET", "api/time-schedules/1", null!],
        ["POST", "api/time-schedules", """{"title":"Office"}"""],
        ["PUT", "api/time-schedules/1", """{"title":"Office"}"""],
        ["DELETE", "api/time-schedules/1", null!],
        ["POST", "api/time-schedules/1/shifts", """{"title":"Day","startTime":"09:00","endTime":"17:00"}"""],
        ["PUT", "api/time-schedules/1/shifts/2", """{"title":"Day","startTime":"09:00","endTime":"17:00"}"""],
        ["DELETE", "api/time-schedules/1/shifts/2", null!],
        ["POST", "api/time-schedules/1/teams", """{"title":"Weekend"}"""],
        ["PUT", "api/time-schedules/1/teams/3", """{"title":"Weekend"}"""],
        ["DELETE", "api/time-schedules/1/teams/3", null!],
        ["PUT", "api/time-schedules/1/teams/3/roster", """{"cells":[{"cycleDay":1,"shiftId":2}]}"""],
        ["PUT", "api/time-schedules/1/teams/3/members", """{"employeeIds":[5]}"""],
    ];

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task EveryEndpoint_IsForHrAdministratorsOnly(string method, string url, string? body)
    {
        foreach (var role in new[] { AttendanceRoles.Employee, AttendanceRoles.Manager, AttendanceRoles.PayrollAdministrator, AttendanceRoles.IntegrationAdministrator })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(method, url, TestApi.As(7, role), body)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(method, url, null, body)).StatusCode);
        Assert.Empty(_schedules.Calls);

        Assert.NotEqual(HttpStatusCode.Forbidden, (await Send(method, url, Hr, body)).StatusCode);
        Assert.Single(_schedules.Calls);
    }

    [Fact]
    public async Task Create_Answers201WithTheId_AndARefusalAs400KeyedByTheInputsProperties()
    {
        _schedules.Result = SaveResult.Success(42);
        var created = await Send("POST", "api/time-schedules", Hr, """{"title":"Office"}""");

        _schedules.Result = SaveResult.Invalid("Title", "Another time schedule has this name.");
        var refused = await Send("POST", "api/time-schedules", Hr, """{"title":"Office"}""");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(42, (await created.Content.ReadFromJsonAsync<CreatedResponse>())!.Id);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("Title", (await refused.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys);
    }

    [Theory]
    [InlineData("PUT", "api/time-schedules/1", """{"title":"Office"}""")]
    [InlineData("DELETE", "api/time-schedules/1", null)]
    [InlineData("PUT", "api/time-schedules/1/shifts/2", """{"title":"Day","startTime":"09:00","endTime":"17:00"}""")]
    [InlineData("DELETE", "api/time-schedules/1/shifts/2", null)]
    [InlineData("PUT", "api/time-schedules/1/teams/3", """{"title":"Weekend"}""")]
    [InlineData("DELETE", "api/time-schedules/1/teams/3", null)]
    [InlineData("PUT", "api/time-schedules/1/teams/3/roster", """{"cells":[]}""")]
    [InlineData("PUT", "api/time-schedules/1/teams/3/members", """{"employeeIds":[]}""")]
    public async Task ChangesAnswer204_404_Or400(string method, string url, string? body)
    {
        _schedules.Result = SaveResult.Success(1);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(method, url, Hr, body)).StatusCode);

        _schedules.Result = SaveResult.Missing();
        Assert.Equal(HttpStatusCode.NotFound, (await Send(method, url, Hr, body)).StatusCode);

        _schedules.Result = SaveResult.Invalid("id", "It is still used.");
        var refused = await Send(method, url, Hr, body);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("still used", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheRoutesCarryTheIdsToTheService_AndTheBodiesBind()
    {
        await Send("PUT", "api/time-schedules/4/teams/9/roster", Hr, """{"cells":[{"cycleDay":2,"shiftId":7},{"cycleDay":3,"shiftId":7}]}""");
        await Send("PUT", "api/time-schedules/4/teams/9/members", Hr, """{"employeeIds":[5,6]}""");
        await Send("POST", "api/time-schedules/4/shifts", Hr, """{"title":"Night","startTime":"22:00","endTime":"06:00","breakTime":true,"breakStartTime":"02:00","breakEndTime":"03:00","overtime":true}""");

        Assert.Equal(["roster 4/9 2:7,3:7", "members 4/9 5,6", "addshift 4 Night 22:00-06:00 break 02:00-03:00 overtime"], _schedules.Calls);
    }

    [Fact]
    public async Task ADetail_IsReturnedOrNotFound()
    {
        _schedules.Detail = new TimeScheduleDetail { Id = 4, Settings = new TimeScheduleInput { Title = "Office" } };
        var found = await Send("GET", "api/time-schedules/4", Hr, null);
        _schedules.Detail = null;
        var missing = await Send("GET", "api/time-schedules/4", Hr, null);

        Assert.Equal("Office", (await found.Content.ReadFromJsonAsync<TimeScheduleDetail>())!.Settings.Title);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
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

    private sealed class FakeSchedules : ITimeScheduleService
    {
        public List<string> Calls { get; } = [];

        public SaveResult Result { get; set; } = SaveResult.Success(1);

        public TimeScheduleDetail? Detail { get; set; } = new() { Id = 1 };

        public Task<IReadOnlyList<TimeScheduleListItem>> ListAsync(CancellationToken cancellationToken)
        {
            Calls.Add("list");
            return Task.FromResult<IReadOnlyList<TimeScheduleListItem>>([]);
        }

        public Task<TimeScheduleDetail?> GetAsync(int id, CancellationToken cancellationToken)
        {
            Calls.Add($"get {id}");
            return Task.FromResult(Detail);
        }

        public Task<SaveResult> CreateAsync(TimeScheduleInput input, CancellationToken cancellationToken) => Record("create");

        public Task<SaveResult> UpdateAsync(int id, TimeScheduleInput input, CancellationToken cancellationToken) => Record("update");

        public Task<SaveResult> DeleteAsync(int id, CancellationToken cancellationToken) => Record("delete");

        public Task<SaveResult> AddShiftAsync(int scheduleId, ScheduleShiftInput input, CancellationToken cancellationToken) =>
            Record($"addshift {scheduleId} {input.Title} {input.StartTime}-{input.EndTime}{(input.BreakTime ? $" break {input.BreakStartTime}-{input.BreakEndTime}" : string.Empty)}{(input.Overtime ? " overtime" : string.Empty)}");

        public Task<SaveResult> UpdateShiftAsync(int scheduleId, int shiftId, ScheduleShiftInput input, CancellationToken cancellationToken) => Record("updateshift");

        public Task<SaveResult> DeleteShiftAsync(int scheduleId, int shiftId, CancellationToken cancellationToken) => Record("deleteshift");

        public Task<SaveResult> AddTeamAsync(int scheduleId, ScheduleTeamInput input, CancellationToken cancellationToken) => Record("addteam");

        public Task<SaveResult> RenameTeamAsync(int scheduleId, int teamId, ScheduleTeamInput input, CancellationToken cancellationToken) => Record("renameteam");

        public Task<SaveResult> DeleteTeamAsync(int scheduleId, int teamId, CancellationToken cancellationToken) => Record("deleteteam");

        public Task<SaveResult> SaveRosterAsync(int scheduleId, int teamId, ScheduleRosterInput input, CancellationToken cancellationToken) =>
            Record($"roster {scheduleId}/{teamId} {string.Join(",", input.Cells.Select(c => $"{c.CycleDay}:{c.ShiftId}"))}");

        public Task<SaveResult> SetTeamMembersAsync(int scheduleId, int teamId, ScheduleMembersInput input, CancellationToken cancellationToken) =>
            Record($"members {scheduleId}/{teamId} {string.Join(",", input.EmployeeIds)}");

        private Task<SaveResult> Record(string call)
        {
            Calls.Add(call);
            return Task.FromResult(Result);
        }
    }
}
