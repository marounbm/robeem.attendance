using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using robeem.Api.Security;
using robeem.Application.HR.Employees;
using robeem.Contracts.Common;
using robeem.Contracts.HR.Employees;
using robeem.Contracts.Security;

namespace robeem.Tests.Api;

/// <summary>The employee list behind the Web's "act as" switcher: only while the development stand-in user is on.</summary>
public sealed class DevelopmentActorsEndpointTests
{
    [Fact]
    public async Task WhileTheStandInUserIsOn_AnyoneCanListTheEmployees_EvenWithNoSignIn()
    {
        await using var api = Start(enabled: true);

        // Anonymous, and also for someone acting as a plain Employee, who can't use the HR endpoint.
        var anonymous = await api.Send(HttpMethod.Get, "api/development/actors", null);
        var employee = await api.Send(HttpMethod.Get, "api/development/actors", TestApi.As(7, AttendanceRoles.Employee));

        Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, employee.StatusCode);
        var actors = await anonymous.Content.ReadFromJsonAsync<List<ActorOption>>();
        Assert.Equal([(3, "Grace Hopper (M-1)"), (4, "Alan Turing (H-1)")], actors!.Select(a => (a.Id, a.Name)));
    }

    [Fact]
    public async Task WithoutTheStandInUser_ItIsA404()
    {
        await using var api = Start(enabled: false);

        Assert.Equal(HttpStatusCode.NotFound, (await api.Send(HttpMethod.Get, "api/development/actors", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Send(HttpMethod.Get, "api/development/actors", TestApi.As(7, AttendanceRoles.HRAdministrator))).StatusCode);
    }

    [Fact]
    public async Task TheStandInUser_IsReportedWithItsEmployeeAndRoles_AndIsA404WhenOff()
    {
        await using var on = Start(enabled: true);
        await using var off = Start(enabled: false);

        var response = await on.Send(HttpMethod.Get, "api/development/actors/stand-in", null);
        var gone = await off.Send(HttpMethod.Get, "api/development/actors/stand-in", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var standIn = await response.Content.ReadFromJsonAsync<StandInActor>();
        Assert.Null(standIn!.EmployeeId);
        Assert.Equal(AttendanceRoles.All.Order(), standIn.Roles.Order());
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    private static TestApi Start(bool enabled) => TestApi.Start(services =>
    {
        services.AddSingleton<IEmployeeService>(new FakeEmployees());
        services.AddSingleton<IOptions<ApiAuthenticationOptions>>(Options.Create(new ApiAuthenticationOptions
        {
            DevelopmentUser = new ApiAuthenticationOptions.DevelopmentUserSettings { Enabled = enabled },
        }));
    });

    private sealed class FakeEmployees : IEmployeeService
    {
        public Task<IReadOnlyList<ManagerOption>> ListManagerOptionsAsync(int? excludeEmployeeId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ManagerOption>>(
            [
                new ManagerOption { Id = 3, Name = "Grace Hopper (M-1)" },
                new ManagerOption { Id = 4, Name = "Alan Turing (H-1)" },
            ]);

        public Task<PagedResult<EmployeeListItem>> ListAsync(string? search, bool includeInactive, int page, int pageSize, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<EmployeeInput?> GetAsync(int id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ScheduleOption>> ListSchedulesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<EmployeeSaveResult> CreateAsync(EmployeeInput input, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<EmployeeSaveResult> UpdateAsync(int id, EmployeeInput input, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
