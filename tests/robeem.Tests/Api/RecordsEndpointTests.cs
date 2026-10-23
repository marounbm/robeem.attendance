using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.DataModel;

namespace robeem.Tests.Api;

/// <summary>The records endpoints (screens phase 5): roles, the viewer built from the token, and the status codes.</summary>
public sealed class RecordsEndpointTests : IAsyncDisposable
{
    private static readonly string Hr = TestApi.As(1, AttendanceRoles.HRAdministrator);

    private readonly FakeRecords _records = new();
    private readonly TestApi _api;

    public RecordsEndpointTests()
    {
        _api = TestApi.Start(services => services.AddSingleton<IAttendanceRecordService>(_records));
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task ListAndDay_AreForEmployeesManagersAndHr_AndReprocessIsForHrOnly()
    {
        foreach (var role in new[] { AttendanceRoles.Employee, AttendanceRoles.Manager, AttendanceRoles.HRAdministrator })
        {
            Assert.Equal(HttpStatusCode.OK, (await _api.Send(HttpMethod.Get, "api/attendance/records", TestApi.As(7, role))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _api.Send(HttpMethod.Get, "api/attendance/records/7/2026-10-05", TestApi.As(7, role))).StatusCode);
        }

        foreach (var role in new[] { AttendanceRoles.PayrollAdministrator, AttendanceRoles.IntegrationAdministrator })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await _api.Send(HttpMethod.Get, "api/attendance/records", TestApi.As(7, role))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await _api.Send(HttpMethod.Get, "api/attendance/records/7/2026-10-05", TestApi.As(7, role))).StatusCode);
        }

        var before = _records.Reprocessed.Count;
        foreach (var role in new[] { AttendanceRoles.Employee, AttendanceRoles.Manager, AttendanceRoles.PayrollAdministrator, AttendanceRoles.IntegrationAdministrator })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await _api.Send(HttpMethod.Post, "api/attendance/records/7/2026-10-05/reprocess", TestApi.As(7, role))).StatusCode);
        }

        Assert.Equal(before, _records.Reprocessed.Count);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Send(HttpMethod.Get, "api/attendance/records", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _api.Send(HttpMethod.Post, "api/attendance/records/7/2026-10-05/reprocess", Hr)).StatusCode);
    }

    [Fact]
    public async Task TheViewer_IsBuiltFromTheTokensRolesAndEmployee_AndTheFilterFromTheQuery()
    {
        await _api.Send(HttpMethod.Get, "api/attendance/records?from=2026-10-01&to=2026-10-31&employeeId=5&status=pendingreview&onlyWithExceptions=true&page=3&pageSize=20", TestApi.As(12, AttendanceRoles.Employee, AttendanceRoles.Manager));

        Assert.Equal(new ExceptionViewer(12, true, true, false), _records.Viewer);
        Assert.Equal(new RecordFilter(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), 5, AttendanceRecordStatus.PendingReview, true, 3, 20), _records.Filter);
    }

    [Fact]
    public async Task TheListDefaultsToTheFirstPageOfTwentyFive_AndRefusesAnUnknownStatus()
    {
        await _api.Send(HttpMethod.Get, "api/attendance/records", Hr);
        var asked = _records.Filter!;
        var refused = await _api.Send(HttpMethod.Get, "api/attendance/records?status=Weird", Hr);

        Assert.Equal((1, 25), (asked.Page, asked.PageSize));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("status", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheEmployeeOptions_AreForTheSameViewersAsTheList_ScopedToWhoIsAsking()
    {
        var employee = await _api.Send(HttpMethod.Get, "api/attendance/records/employee-options", TestApi.As(12, AttendanceRoles.Employee));
        var viewer = _records.Viewer;
        var payroll = await _api.Send(HttpMethod.Get, "api/attendance/records/employee-options", TestApi.As(12, AttendanceRoles.PayrollAdministrator));
        var anonymous = await _api.Send(HttpMethod.Get, "api/attendance/records/employee-options", null);
        var unlinked = await _api.Send(HttpMethod.Get, "api/attendance/records/employee-options", TestApi.As(null, AttendanceRoles.Manager));

        Assert.Equal(HttpStatusCode.OK, employee.StatusCode);
        Assert.Equal(new ExceptionViewer(12, true, false, false), viewer);
        Assert.Equal("Ada Lovelace (E-1)", (await employee.Content.ReadFromJsonAsync<List<EmployeeOption>>())!.Single().Name);
        Assert.Equal(HttpStatusCode.Forbidden, payroll.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unlinked.StatusCode);
    }

    [Fact]
    public async Task AnAccountWithoutAnEmployee_IsServedOnlyWhenItIsHr_WhoSeeEveryoneAndNeedNoEmployeeOfTheirOwn()
    {
        var hr = await _api.Send(HttpMethod.Get, "api/attendance/records", TestApi.As(null, AttendanceRoles.HRAdministrator));
        var hrViewer = _records.Viewer;
        var hrDay = await _api.Send(HttpMethod.Get, "api/attendance/records/7/2026-10-05", TestApi.As(null, AttendanceRoles.HRAdministrator));

        // The development stand-in with every role and no employee is HR for this purpose; its other roles add no scope.
        await _api.Send(HttpMethod.Get, "api/attendance/records", TestApi.As(null, AttendanceRoles.Employee, AttendanceRoles.Manager, AttendanceRoles.HRAdministrator));

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (hr.StatusCode, hrDay.StatusCode));
        Assert.Equal(new ExceptionViewer(0, false, false, true), hrViewer);
        Assert.Equal(new ExceptionViewer(0, false, false, true), _records.Viewer);
    }

    [Theory]
    [InlineData(AttendanceRoles.Employee)]
    [InlineData(AttendanceRoles.Manager)]
    public async Task AnEmployeeOrManagerWithoutAnEmployee_IsRefused_ForTheirScopeDependsOnWhoTheyAre(string role)
    {
        var list = await _api.Send(HttpMethod.Get, "api/attendance/records", TestApi.As(null, role));
        var day = await _api.Send(HttpMethod.Get, "api/attendance/records/7/2026-10-05", TestApi.As(null, role));

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (list.StatusCode, day.StatusCode));
        Assert.Null(_records.Viewer);
    }

    [Fact]
    public async Task ADayThatIsMissingOrNotVisible_Is404_ForTheDayAndForReprocessing()
    {
        _records.Missing = true;

        Assert.Equal(HttpStatusCode.NotFound, (await _api.Send(HttpMethod.Get, "api/attendance/records/99/2026-10-05", Hr)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.Send(HttpMethod.Post, "api/attendance/records/99/2026-10-05/reprocess", Hr)).StatusCode);
    }

    [Fact]
    public async Task TheDayIsReadFromTheRoute_AndReprocessingAnswersWhatItDid()
    {
        var day = await _api.Send(HttpMethod.Get, "api/attendance/records/7/2026-10-05", TestApi.As(7, AttendanceRoles.Employee));
        var reprocess = await _api.Send(HttpMethod.Post, "api/attendance/records/7/2026-10-05/reprocess", Hr);

        Assert.Equal(7, (await day.Content.ReadFromJsonAsync<RecordDayDetail>())!.EmployeeId);
        Assert.Equal((7, new DateTime(2026, 10, 5)), _records.Reprocessed.Single());
        Assert.Equal(2, (await reprocess.Content.ReadFromJsonAsync<ReprocessResponse>())!.RecordsWritten);
    }

    private sealed class FakeRecords : IAttendanceRecordService
    {
        public ExceptionViewer? Viewer { get; private set; }

        public RecordFilter? Filter { get; private set; }

        public bool Missing { get; set; }

        public List<(int EmployeeId, DateTime Date)> Reprocessed { get; } = [];

        public Task<PagedResult<RecordDayListItem>> ListDaysAsync(RecordFilter filter, ExceptionViewer viewer, CancellationToken cancellationToken)
        {
            Filter = filter;
            Viewer = viewer;
            return Task.FromResult(new PagedResult<RecordDayListItem> { Page = filter.Page, PageSize = filter.PageSize });
        }

        public Task<IReadOnlyList<EmployeeOption>> ListEmployeeOptionsAsync(ExceptionViewer viewer, CancellationToken cancellationToken)
        {
            Viewer = viewer;
            return Task.FromResult<IReadOnlyList<EmployeeOption>>([new EmployeeOption { Id = 7, Name = "Ada Lovelace (E-1)" }]);
        }

        public Task<RecordDayDetail?> GetDayAsync(int employeeId, DateTime date, ExceptionViewer viewer, CancellationToken cancellationToken)
        {
            Viewer = viewer;
            return Task.FromResult<RecordDayDetail?>(Missing ? null : new RecordDayDetail { EmployeeId = employeeId, AttendanceDate = date });
        }

        public Task<ReprocessResponse?> ReprocessAsync(int employeeId, DateTime date, CancellationToken cancellationToken)
        {
            Reprocessed.Add((employeeId, date));
            return Task.FromResult<ReprocessResponse?>(Missing ? null : new ReprocessResponse { RecordsWritten = 2 });
        }
    }
}
