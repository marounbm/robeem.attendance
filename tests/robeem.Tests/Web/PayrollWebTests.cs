using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.Web.Actors;
using robeem.Web.ApiClients;
using robeem.Web.Controllers;
using robeem.Web.Models;
using robeem.Web.Navigation;

namespace robeem.Tests.Web;

/// <summary>The pay period and payroll screens (screens phase 3): the typed clients, the controllers and the CSV.</summary>
public sealed class PayrollWebTests
{
    private static readonly PayPeriodItem Open = new() { Id = 1, StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2026, 10, 31) };
    private static readonly PayPeriodItem Closed = new() { Id = 2, StartDate = new DateTime(2026, 9, 1), EndDate = new DateTime(2026, 9, 30), IsClosed = true };

    [Fact]
    public async Task ThePeriodsClient_ReadsTheList_AndUsesTheRoutesOfEveryAction()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, """[{"id":1,"startDate":"2026-10-01T00:00:00","endDate":"2026-10-31T00:00:00","isClosed":true}]""");
        var client = new PayPeriodsApiClient(handler.CreateClient());

        var list = await client.ListAsync(CancellationToken.None);
        await client.CreateAsync(new PayPeriodInput { StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2026, 10, 31) }, CancellationToken.None);
        await client.CloseAsync(1, CancellationToken.None);
        await client.ReopenAsync(1, "late timesheet", CancellationToken.None);

        Assert.True(list.Single().IsClosed);
        Assert.Equal(
            [("GET", "/api/attendance/pay-periods"), ("POST", "/api/attendance/pay-periods"), ("POST", "/api/attendance/pay-periods/1/close"), ("POST", "/api/attendance/pay-periods/1/reopen")],
            handler.Requests.Select(r => (r.Method.Method, r.RequestUri!.PathAndQuery)));
        Assert.Contains("late timesheet", handler.Bodies[3]);
    }

    [Fact]
    public async Task CreateAndReopen_ReturnTheIdOrTheFieldErrors_OrNotFound()
    {
        var problem = """{"title":"x","status":400,"errors":{"StartDate":["The period overlaps another one."],"Reason":["A reason is required."],"id":["The pay period is not closed."]}}""";
        var created = new PayPeriodsApiClient(new StubHttpHandler(HttpStatusCode.Created, """{"id":7}""").CreateClient());
        var refused = new PayPeriodsApiClient(new StubHttpHandler(HttpStatusCode.BadRequest, problem, "application/problem+json").CreateClient());
        var gone = new PayPeriodsApiClient(new StubHttpHandler(HttpStatusCode.NotFound).CreateClient());

        var ok = await created.CreateAsync(new PayPeriodInput(), CancellationToken.None);
        var overlap = await refused.CreateAsync(new PayPeriodInput(), CancellationToken.None);
        var noReason = await refused.ReopenAsync(1, string.Empty, CancellationToken.None);

        Assert.Equal((true, 7), (ok.Succeeded, ok.Id));
        Assert.Equal("The period overlaps another one.", overlap.Errors[nameof(PayPeriodInput.StartDate)]);
        Assert.Equal("A reason is required.", noReason.Errors[nameof(ReopenPayPeriodRequest.Reason)]);
        Assert.Contains("not closed", noReason.MessageText);
        Assert.True((await gone.CloseAsync(9, CancellationToken.None)).NotFound);
    }

    [Fact]
    public async Task ThePayrollClient_AsksForTheDatesAsPlainDays_AndReadsTheDays()
    {
        var json = """[{"employeeId":3,"employeeNumber":"E-3","attendanceDate":"2026-10-05T00:00:00","totalWorkingHours":"08:00:00","totalOvertimeHours":"01:30:00","totalBreakHours":"00:30:00","shifts":[]}]""";
        var handler = new StubHttpHandler(HttpStatusCode.OK, json);

        var days = await new PayrollApiClient(handler.CreateClient()).GetApprovedAsync(new DateTime(2026, 10, 1, 13, 0, 0), new DateTime(2026, 10, 31), null, CancellationToken.None);

        Assert.Equal("/api/attendance/payroll/approved?from=2026-10-01&to=2026-10-31", handler.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal(("E-3", TimeSpan.FromHours(1.5)), (days.Single().EmployeeNumber, days.Single().TotalOvertimeHours));
    }

    [Fact]
    public async Task ThePeriodsPage_ProposesTheDayAfterTheLatestPeriod()
    {
        var controller = WithTempData(new PayPeriodsController(new FakePeriods([Closed, Open])));

        var model = (PayPeriodsIndexViewModel)((ViewResult)await controller.Index(CancellationToken.None)).Model!;

        Assert.Equal((new DateTime(2026, 11, 1), new DateTime(2026, 11, 30)), (model.Input.StartDate, model.Input.EndDate));
    }

    [Fact]
    public async Task CreatingAPeriod_RedirectsOnSuccess_AndShowsTheFormWithTheErrorsOtherwise()
    {
        var periods = new FakePeriods([]) { Save = ApiSaveResult.Success(5) };
        var controller = WithTempData(new PayPeriodsController(periods));
        var input = new PayPeriodInput { StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2026, 10, 31) };

        var created = await controller.Create(new PayPeriodsIndexViewModel { Input = input }, CancellationToken.None);
        Assert.IsType<RedirectToActionResult>(created);
        Assert.Contains("2026-10-01 to 2026-10-31", (string)controller.TempData["StatusMessage"]!);

        periods.Save = ApiSaveResult.Invalid(new Dictionary<string, string> { [nameof(PayPeriodInput.StartDate)] = "The period overlaps another one." });
        var again = await controller.Create(new PayPeriodsIndexViewModel { Input = input }, CancellationToken.None);

        Assert.Equal("Index", ((ViewResult)again).ViewName);
        Assert.Equal("The period overlaps another one.", controller.ModelState["Input.StartDate"]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task Reopening_NeedsAReason_BeforeTheApiIsAsked()
    {
        var periods = new FakePeriods([Closed]);
        var controller = WithTempData(new PayPeriodsController(periods));

        await controller.Reopen(2, "  ", CancellationToken.None);
        Assert.Contains("Give a reason", (string)controller.TempData["ErrorMessage"]!);
        Assert.Empty(periods.Calls);

        await controller.Reopen(2, "late timesheet", CancellationToken.None);
        Assert.Equal([("reopen", 2, "late timesheet")], periods.Calls);
        Assert.Contains("reopened", (string)controller.TempData["StatusMessage"]!);
    }

    [Fact]
    public async Task ARefusedClose_IsShownAsAnError_AndAnUnknownPeriodIsNotFound()
    {
        var periods = new FakePeriods([Open]) { Save = ApiSaveResult.Invalid(new Dictionary<string, string> { [string.Empty] = "The pay period is already closed." }) };
        var controller = WithTempData(new PayPeriodsController(periods));

        await controller.Close(1, CancellationToken.None);
        Assert.Equal("The pay period is already closed.", controller.TempData["ErrorMessage"]);

        periods.Save = ApiSaveResult.Missing();
        Assert.IsType<NotFoundResult>(await controller.Close(9, CancellationToken.None));
    }

    [Fact]
    public async Task Payroll_AsksForAChosenPeriodsDates_AndNotingWhenNothingWasChosen()
    {
        var payroll = new FakePayroll();
        var controller = WithTempData(new PayrollController(payroll, new FakePeriods([Closed, Open])));

        var none = (PayrollIndexViewModel)((ViewResult)await controller.Index(null, null, null, null, 0, 0, CancellationToken.None)).Model!;
        Assert.Null(none.Days);
        Assert.Empty(payroll.Asked);

        var chosen = (PayrollIndexViewModel)((ViewResult)await controller.Index(1, null, null, null, 0, 0, CancellationToken.None)).Model!;
        Assert.Equal([((DateTime?)new DateTime(2026, 10, 1), (DateTime?)new DateTime(2026, 10, 31))], payroll.Asked);
        Assert.Empty(chosen.Days!);
        Assert.False(chosen.PeriodIsClosed);
    }

    [Fact]
    public async Task Payroll_NeedsAnEmployeeOrATimeRange_AndAnEmployeeAloneReadsEveryDate()
    {
        var payroll = new FakePayroll { Days = [Day(1, "E-1", 5, 8, 0), Day(2, "E-2", 5, 8, 0)] };
        var controller = WithTempData(new PayrollController(payroll, new FakePeriods([Open])));

        var neither = (PayrollIndexViewModel)((ViewResult)await controller.Index(null, null, null, "  ", 1, 20, CancellationToken.None)).Model!;
        var oneDate = (PayrollIndexViewModel)((ViewResult)await controller.Index(null, new DateTime(2026, 10, 1), null, null, 1, 20, CancellationToken.None)).Model!;
        var oneDateAndAnEmployee = (PayrollIndexViewModel)((ViewResult)await controller.Index(null, null, new DateTime(2026, 10, 31), "E-1", 1, 20, CancellationToken.None)).Model!;
        Assert.Empty(payroll.Asked);
        Assert.False(neither.CanQuery);
        Assert.Null(neither.Days);
        Assert.Contains("Give both dates", oneDate.Error);
        Assert.Contains("Give both dates", oneDateAndAnEmployee.Error);

        var onlyAnEmployee = (PayrollIndexViewModel)((ViewResult)await controller.Index(null, null, null, "E-1", 1, 20, CancellationToken.None)).Model!;
        var onlyDates = (PayrollIndexViewModel)((ViewResult)await controller.Index(null, new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), null, 1, 20, CancellationToken.None)).Model!;

        Assert.Equal([((DateTime?)null, (DateTime?)null), (new DateTime(2026, 10, 1), new DateTime(2026, 10, 31))], payroll.Asked);
        Assert.Equal((true, 1, null), (onlyAnEmployee.CanQuery, onlyAnEmployee.Total, onlyAnEmployee.Error));
        Assert.Equal((true, 2), (onlyDates.CanQuery, onlyDates.Total));
    }

    [Fact]
    public async Task TheCsv_NeedsTheSameQuery_AndNamesAnAllDatesFile()
    {
        var payroll = new FakePayroll { Days = [Day(1, "E-1", 5, 8, 0)] };
        var controller = WithTempData(new PayrollController(payroll, new FakePeriods([Open])));

        var refused = await controller.Export(null, null, null, null, CancellationToken.None);
        var file = Assert.IsType<FileContentResult>(await controller.Export(null, null, null, "E-1", CancellationToken.None));

        Assert.IsType<RedirectToActionResult>(refused);
        Assert.Contains("Choose an employee", (string)controller.TempData["ErrorMessage"]!);
        Assert.Equal("payroll-all-dates.csv", file.FileDownloadName);
    }

    [Fact]
    public async Task Payroll_ExplainsAClosedPeriod_AUnknownOne_AndABackwardsRange()
    {
        var payroll = new FakePayroll();
        var controller = WithTempData(new PayrollController(payroll, new FakePeriods([Closed, Open])));

        var closed = (PayrollIndexViewModel)((ViewResult)await controller.Index(2, null, null, null, 1, 20, CancellationToken.None)).Model!;
        var unknown = (PayrollIndexViewModel)((ViewResult)await controller.Index(99, null, null, null, 1, 20, CancellationToken.None)).Model!;
        var backwards = (PayrollIndexViewModel)((ViewResult)await controller.Index(null, new DateTime(2026, 10, 31), new DateTime(2026, 10, 1), null, 1, 20, CancellationToken.None)).Model!;

        Assert.True(closed.PeriodIsClosed);
        Assert.Equal("That pay period doesn't exist.", unknown.Error);
        Assert.Equal("The period ends before it starts.", backwards.Error);
        Assert.Single(payroll.Asked);
    }

    [Fact]
    public async Task Payroll_TotalsTheDaysPerEmployee()
    {
        var payroll = new FakePayroll { Days = [Day(2, "E-2", 5, 8, 0), Day(1, "E-1", 5, 8, 1), Day(1, "E-1", 6, 7.5, 0)] };
        var controller = WithTempData(new PayrollController(payroll, new FakePeriods([Open])));

        var model = (PayrollIndexViewModel)((ViewResult)await controller.Index(1, null, null, null, 1, 20, CancellationToken.None)).Model!;

        Assert.Equal(["E-1", "E-2"], model.Employees.Select(e => e.EmployeeNumber));
        Assert.Equal(["Employee E-1", "Employee E-2"], model.Employees.Select(e => e.EmployeeName));
        var first = model.Employees[0];
        Assert.Equal((2, TimeSpan.FromHours(15.5), TimeSpan.FromHours(1)), (first.Days, first.WorkingHours, first.OvertimeHours));
    }

    [Fact]
    public async Task Payroll_AsksForOnePageOfAListedSize_AndTheTotalsCoverEveryPage()
    {
        var payroll = new FakePayroll { Days = [.. Enumerable.Range(1, 25).Select(day => Day(1, "E-1", day, 8, 0))] };
        var controller = WithTempData(new PayrollController(payroll, new FakePeriods([Open])));

        var model = (PayrollIndexViewModel)((ViewResult)await controller.Index(1, null, null, null, 2, 10, CancellationToken.None)).Model!;
        await controller.Index(1, null, null, null, 1, 7, CancellationToken.None);

        Assert.Equal([(2, 10), (1, 20)], payroll.PageAsks);
        Assert.Equal((2, 10, 25), (model.Page, model.PageSize, model.Total));
        Assert.Equal(10, model.Days!.Count);
        Assert.Equal(11, model.Days[0].AttendanceDate.Day);
        Assert.Equal(25, Assert.Single(model.Employees).Days);
        Assert.Equal(TimeSpan.FromHours(200), model.Employees[0].WorkingHours);
    }

    [Fact]
    public async Task Payroll_NarrowsTheDaysAndTheirTotals_ToOneEmployee_AndTheCsvFollows()
    {
        var payroll = new FakePayroll { Days = [Day(1, "E-1", 5, 8, 1), Day(2, "E-2", 5, 8, 0), Day(2, "E-2", 6, 7, 0)] };
        var controller = WithTempData(new PayrollController(payroll, new FakePeriods([Open])));

        var byNumber = (PayrollIndexViewModel)((ViewResult)await controller.Index(1, null, null, " e-2 ", 1, 20, CancellationToken.None)).Model!;
        var byName = (PayrollIndexViewModel)((ViewResult)await controller.Index(1, null, null, "EMPLOYEE E-1", 1, 20, CancellationToken.None)).Model!;
        var nobody = (PayrollIndexViewModel)((ViewResult)await controller.Index(1, null, null, "zzz", 1, 20, CancellationToken.None)).Model!;
        var file = Assert.IsType<FileContentResult>(await controller.Export(1, null, null, "E-2", CancellationToken.None));

        Assert.Equal(("e-2", 2), (byNumber.Employee, byNumber.Total));
        Assert.Equal(["E-2"], byNumber.Employees.Select(e => e.EmployeeNumber));
        Assert.Equal(15, byNumber.Employees.Single().WorkingHours.TotalHours);
        Assert.Equal(["E-1"], byName.Days!.Select(d => d.EmployeeNumber));
        Assert.Equal((0, 0), (nobody.Total, nobody.Employees.Count));
        var csv = Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("E-2,2026-10-05", csv);
        Assert.DoesNotContain("E-1,", csv);
    }

    [Fact]
    public async Task ThePayrollPager_KeepsThePeriodOrTheDates_AndThePageSize()
    {
        var controller = WithTempData(new PayrollController(new FakePayroll(), new FakePeriods([Open])));

        var byPeriod = (PayrollIndexViewModel)((ViewResult)await controller.Index(1, null, null, "ada", 1, 50, CancellationToken.None)).Model!;
        var byDates = (PayrollIndexViewModel)((ViewResult)await controller.Index(null, new DateTime(2026, 10, 5), new DateTime(2026, 10, 9), null, 1, 50, CancellationToken.None)).Model!;

        Assert.Equal(("1", "50", "ada"), (byPeriod.Pager.RouteValues["periodId"], byPeriod.Pager.RouteValues["pageSize"], byPeriod.Pager.RouteValues["employee"]));
        Assert.False(byPeriod.Pager.RouteValues.ContainsKey("from"));
        Assert.False(byDates.Pager.RouteValues.ContainsKey("employee"));
        Assert.Equal(("2026-10-05", "2026-10-09"), (byDates.Pager.RouteValues["from"], byDates.Pager.RouteValues["to"]));
        Assert.False(byDates.Pager.RouteValues.ContainsKey("periodId"));
    }

    [Fact]
    public async Task ThePayrollClient_AsksForAPage_AndReadsItsDaysAndTotals()
    {
        var json = """{"days":{"items":[{"employeeId":3,"employeeNumber":"E-3","employeeName":"Ada","attendanceDate":"2026-10-05T00:00:00","totalWorkingHours":"08:00:00","totalOvertimeHours":"00:00:00","totalBreakHours":"00:00:00","shifts":[]}],"total":41,"page":2,"pageSize":20},"employees":[{"employeeId":3,"employeeNumber":"E-3","employeeName":"Ada","days":41,"workingHours":"13.16:00:00","overtimeHours":"00:00:00","breakHours":"00:00:00"}]}""";
        var handler = new StubHttpHandler(HttpStatusCode.OK, json);

        var page = await new PayrollApiClient(handler.CreateClient()).GetApprovedPageAsync(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), "ada lo", 2, 20, CancellationToken.None);

        Assert.Equal("/api/attendance/payroll/approved/page?from=2026-10-01&to=2026-10-31&page=2&pageSize=20&employee=ada%20lo", handler.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal((41, 2, 3), (page.Days.Total, page.Days.Page, page.Days.PageCount));
        Assert.Equal(41, Assert.Single(page.Employees).Days);
    }

    [Fact]
    public async Task Export_DownloadsTheSameDaysAsACsvFile_OrExplainsWhyNot()
    {
        var payroll = new FakePayroll { Days = [Day(1, "E-1", 5, 8, 1)] };
        var controller = WithTempData(new PayrollController(payroll, new FakePeriods([Open])));

        var file = Assert.IsType<FileContentResult>(await controller.Export(1, null, null, null, CancellationToken.None));
        var none = await controller.Export(null, null, null, null, CancellationToken.None);

        Assert.Equal(("text/csv", "payroll-2026-10-01-2026-10-31.csv"), (file.ContentType, file.FileDownloadName));
        Assert.Equal([0xEF, 0xBB, 0xBF], file.FileContents.Take(3));
        Assert.Contains("E-1,2026-10-05,8.00,1.00,0.00,0", Encoding.UTF8.GetString(file.FileContents));
        Assert.IsType<RedirectToActionResult>(none);
        Assert.Contains("Choose an employee", (string)controller.TempData["ErrorMessage"]!);
    }

    [Fact]
    public void TheCsv_HasAHeader_OrdersTheDays_AndWritesHoursAsDecimals()
    {
        var csv = PayrollCsv.Build([Day(2, "E-2", 5, 8, 0), Day(1, "E-1", 6, 7.25, 0.5), Day(1, "E-1", 5, 8, 0)]);

        Assert.Equal(
            "EmployeeNumber,Date,WorkingHours,OvertimeHours,BreakHours,Shifts\r\n"
            + "E-1,2026-10-05,8.00,0.00,0.00,0\r\n"
            + "E-1,2026-10-06,7.25,0.50,0.00,0\r\n"
            + "E-2,2026-10-05,8.00,0.00,0.00,0\r\n",
            csv);
    }

    [Theory]
    [InlineData("E-1", "E-1")]
    [InlineData("A,B", "\"A,B\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("+1", "'+1")]
    [InlineData("", "")]
    public void ACsvField_IsQuotedWhenNeeded_AndNeverReadAsAFormula(string value, string expected)
    {
        Assert.Equal(expected, PayrollCsv.Field(value));
    }

    [Fact]
    public void ThePayrollScreens_AreForPayrollAdministratorsOnly()
    {
        var payroll = NavigationMenu.For(new FakeActor(["PayrollAdministrator"])).SelectMany(s => s.Items).Select(i => i.Controller);
        var others = NavigationMenu.For(new FakeActor(["Employee", "Manager", "HRAdministrator", "IntegrationAdministrator"])).SelectMany(s => s.Items).Select(i => i.Controller);

        Assert.Equal(["Payroll", "PayPeriods"], payroll);
        Assert.DoesNotContain(others, c => c is "Payroll" or "PayPeriods");
    }

    private static PayrollAttendanceDay Day(int employeeId, string number, int dayOfOctober, double working, double overtime) =>
        new(employeeId, number, $"Employee {number}", new DateTime(2026, 10, dayOfOctober), TimeSpan.FromHours(working), TimeSpan.FromHours(overtime), TimeSpan.Zero, []);

    private static T WithTempData<T>(T controller)
        where T : Controller
    {
        controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new NullTempDataProvider());
        return controller;
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }

    private sealed class FakeActor : ICurrentActor
    {
        private readonly string[] _roles;

        public FakeActor(string[] roles)
        {
            _roles = roles;
        }

        public bool HasChoice => true;

        public ActingAs? Choice => new(1, "Test", _roles);

        public bool IsInRole(string role) => _roles.Contains(role);

        public bool IsInAnyRole(params string[] roles) => roles.Any(IsInRole);
    }

    private sealed class FakePeriods : IPayPeriodsApiClient
    {
        private readonly IReadOnlyList<PayPeriodItem> _periods;

        public FakePeriods(IReadOnlyList<PayPeriodItem> periods)
        {
            _periods = periods;
        }

        public ApiSaveResult Save { get; set; } = ApiSaveResult.Success(1);

        public List<(string Kind, int Id, string? Reason)> Calls { get; } = [];

        public Task<IReadOnlyList<PayPeriodItem>> ListAsync(CancellationToken cancellationToken) => Task.FromResult(_periods);

        public Task<ApiSaveResult> CreateAsync(PayPeriodInput input, CancellationToken cancellationToken) => Task.FromResult(Save);

        public Task<ApiSaveResult> CloseAsync(int id, CancellationToken cancellationToken) => Task.FromResult(Save);

        public Task<ApiSaveResult> ReopenAsync(int id, string reason, CancellationToken cancellationToken)
        {
            Calls.Add(("reopen", id, reason));
            return Task.FromResult(Save);
        }
    }

    private sealed class FakePayroll : IPayrollApiClient
    {
        public IReadOnlyList<PayrollAttendanceDay> Days { get; set; } = [];

        public List<(DateTime? From, DateTime? To)> Asked { get; } = [];

        public List<(int Page, int PageSize)> PageAsks { get; } = [];

        public List<string?> EmployeeAsks { get; } = [];

        public Task<IReadOnlyList<PayrollAttendanceDay>> GetApprovedAsync(DateTime? from, DateTime? to, string? employee, CancellationToken cancellationToken)
        {
            Asked.Add((from, to));
            EmployeeAsks.Add(employee);
            return Task.FromResult(PayrollApprovedPageService.Filter(Days, employee));
        }

        public Task<PayrollApprovedPage> GetApprovedPageAsync(DateTime? from, DateTime? to, string? employee, int page, int pageSize, CancellationToken cancellationToken)
        {
            Asked.Add((from, to));
            PageAsks.Add((page, pageSize));
            EmployeeAsks.Add(employee);
            return Task.FromResult(PayrollApprovedPageService.Build(PayrollApprovedPageService.Filter(Days, employee), page, pageSize));
        }
    }
}
