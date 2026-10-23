using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.Web.Actors;
using robeem.Web.ApiClients;
using robeem.Web.Controllers;
using robeem.Web.Models;
using robeem.Web.Navigation;

namespace robeem.Tests.Web;

/// <summary>The attendance records screens (screens phase 5): the typed client and the controller.</summary>
public sealed class RecordsWebTests
{
    [Fact]
    public async Task TheRecordsClient_SendsOnlyTheFiltersThatWereGiven_AndUsesTheDayRoutes()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, """{"items":[],"total":0,"page":1,"pageSize":25}""");
        var client = new RecordsApiClient(handler.CreateClient());

        await client.ListAsync(null, null, null, null, false, 1, 25, CancellationToken.None);
        await client.ListAsync(new DateTime(2026, 10, 1, 14, 30, 0), new DateTime(2026, 10, 31), 12, "PendingReview", true, 3, 50, CancellationToken.None);

        var day = new StubHttpHandler(HttpStatusCode.OK, """{"employeeId":7,"employeeName":"Ada Lovelace","processed":true,"versions":[{"id":3,"version":2,"isCurrent":true,"workingHours":"08:00:00"}]}""");
        var detail = await new RecordsApiClient(day.CreateClient()).GetDayAsync(7, new DateTime(2026, 10, 5, 9, 0, 0), CancellationToken.None);

        Assert.Equal(
            ["/api/attendance/records?page=1&pageSize=25", "/api/attendance/records?page=3&pageSize=50&from=2026-10-01&to=2026-10-31&employeeId=12&status=PendingReview&onlyWithExceptions=true"],
            handler.Requests.Select(r => r.RequestUri!.PathAndQuery));
        Assert.Equal("/api/attendance/records/7/2026-10-05", day.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal(TimeSpan.FromHours(8), Assert.Single(detail!.Versions).WorkingHours);
    }

    [Fact]
    public async Task TheRecordsClient_ReadsAPageWithItsTotal_AndTheEmployeeOptions()
    {
        var page = new StubHttpHandler(HttpStatusCode.OK, """{"items":[{"employeeId":7,"employeeName":"Ada Lovelace"}],"total":61,"page":2,"pageSize":25}""");
        var options = new StubHttpHandler(HttpStatusCode.OK, """[{"id":7,"name":"Ada Lovelace (E-1)"}]""");

        var result = await new RecordsApiClient(page.CreateClient()).ListAsync(null, null, null, null, false, 2, 25, CancellationToken.None);
        var employees = await new RecordsApiClient(options.CreateClient()).ListEmployeeOptionsAsync(CancellationToken.None);

        Assert.Equal((61, 2, 25, 3), (result.Total, result.Page, result.PageSize, result.PageCount));
        Assert.Equal(7, Assert.Single(result.Items).EmployeeId);
        Assert.Equal("/api/attendance/records/employee-options", options.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal("Ada Lovelace (E-1)", Assert.Single(employees).Name);
    }

    [Fact]
    public async Task ADayAndAReprocess_MapA404ToNull_AndReprocessPostsToItsRoute()
    {
        var missing = new RecordsApiClient(new StubHttpHandler(HttpStatusCode.NotFound).CreateClient());
        var handler = new StubHttpHandler(HttpStatusCode.OK, """{"recordsWritten":2,"recordsUnchanged":0,"recordsRetired":1,"exceptionsRaised":0,"exceptionsResolved":1}""");

        var result = await new RecordsApiClient(handler.CreateClient()).ReprocessAsync(7, new DateTime(2026, 10, 5), CancellationToken.None);

        Assert.Null(await missing.GetDayAsync(9, new DateTime(2026, 10, 5), CancellationToken.None));
        Assert.Null(await missing.ReprocessAsync(9, new DateTime(2026, 10, 5), CancellationToken.None));
        Assert.Equal(("POST", "/api/attendance/records/7/2026-10-05/reprocess"), (handler.Requests.Single().Method.Method, handler.Requests.Single().RequestUri!.PathAndQuery));
        Assert.Equal((2, 1), (result!.RecordsWritten, result.RecordsRetired));
    }

    [Fact]
    public async Task TheList_TreatsAnUnknownStatusAsAny_AndPassesTheRest()
    {
        var records = new FakeRecords();
        var controller = Controller(records, ["Employee"]);

        var any = (RecordsIndexViewModel)((ViewResult)await controller.Index("Bogus", null, null, false, null, 0, 0, CancellationToken.None)).Model!;
        var askedForAny = records.Status;
        var pending = (RecordsIndexViewModel)((ViewResult)await controller.Index("PendingReview", new DateTime(2026, 10, 1), null, true, 12, 1, 25, CancellationToken.None)).Model!;

        Assert.Equal(("Any", null), (any.Status, askedForAny));
        Assert.Equal("PendingReview", pending.Status);
        Assert.Equal(("PendingReview", true, new DateTime(2026, 10, 1), 12), (records.Status, records.OnlyWithExceptions, records.From, records.EmployeeId));
        Assert.Equal(12, pending.EmployeeId);
    }

    [Fact]
    public async Task ThePage_IsAskedFromTheServer_WithASizeFromTheOfferedOnes_AndShowsWhatTheServerAnswered()
    {
        var records = new FakeRecords { Total = 61, ServerPage = 2 };
        var controller = Controller(records, ["HRAdministrator"]);

        var model = (RecordsIndexViewModel)((ViewResult)await controller.Index(null, null, null, false, 12, 9, 7, CancellationToken.None)).Model!;

        // 7 is not a size on offer, and page 9 was past the end: the server's page is the one shown.
        Assert.Equal((9, RecordsIndexViewModel.DefaultPageSize), (records.PageAsked, records.PageSizeAsked));
        Assert.Equal((2, RecordsIndexViewModel.DefaultPageSize, 61, 12), (model.Page, model.PageSize, model.Total, model.EmployeeId));
        Assert.Equal("Ada Lovelace (E-1)", Assert.Single(model.Employees).Name);

        await controller.Index(null, null, null, false, null, -4, 100, CancellationToken.None);
        Assert.Equal((1, 100), (records.PageAsked, records.PageSizeAsked));
    }

    [Fact]
    public void ThePager_KeepsTheFilters_OnEveryPageLink_AndLinksOnlyTheNearbyPages()
    {
        var model = new RecordsIndexViewModel
        {
            Page = 6,
            PageSize = 10,
            Total = 200,
            Status = "Approved",
            From = new DateTime(2026, 10, 1),
            OnlyWithExceptions = true,
            EmployeeId = 12,
        };

        var pager = model.Pager;

        Assert.Equal((20, 51, 60), (pager.PageCount, pager.FirstItem, pager.LastItem));
        Assert.Equal([1, 4, 5, 6, 7, 8, 20], pager.Pages);
        Assert.Equal(
            new Dictionary<string, string?>
            {
                ["pageSize"] = "10",
                ["status"] = "Approved",
                ["from"] = "2026-10-01",
                ["onlyWithExceptions"] = "true",
                ["employeeId"] = "12",
                ["page"] = "7",
            },
            pager.For(7));
    }

    [Fact]
    public async Task TheDay_OffersReprocessingToHrOnly_AndAnUnknownDayIsNotFound()
    {
        var records = new FakeRecords();

        var asHr = (RecordDayViewModel)((ViewResult)await Controller(records, ["HRAdministrator"]).Day(7, new DateTime(2026, 10, 5), CancellationToken.None)).Model!;
        var asEmployee = (RecordDayViewModel)((ViewResult)await Controller(records, ["Employee"]).Day(7, new DateTime(2026, 10, 5), CancellationToken.None)).Model!;
        records.Missing = true;

        Assert.Equal((true, false), (asHr.CanReprocess, asEmployee.CanReprocess));
        Assert.IsType<NotFoundResult>(await Controller(records, ["HRAdministrator"]).Day(9, new DateTime(2026, 10, 5), CancellationToken.None));
    }

    [Fact]
    public async Task Reprocessing_ReportsWhatChanged_AndReturnsToTheDay()
    {
        var records = new FakeRecords { Result = new ReprocessResponse { RecordsWritten = 1, RecordsRetired = 1 } };
        var controller = Controller(records, ["HRAdministrator"]);

        var changed = (RedirectToActionResult)await controller.Reprocess(7, new DateTime(2026, 10, 5), CancellationToken.None);
        var changedMessage = (string)controller.TempData["StatusMessage"]!;
        records.Result = new ReprocessResponse { RecordsUnchanged = 2 };
        await controller.Reprocess(7, new DateTime(2026, 10, 5), CancellationToken.None);

        Assert.Equal(("Day", 7, "2026-10-05"), (changed.ActionName, changed.RouteValues!["employeeId"], changed.RouteValues["date"]));
        Assert.Contains("1 record(s) written, 1 retired", changedMessage);
        Assert.Equal("The day was calculated again and nothing changed.", controller.TempData["StatusMessage"]);

        records.Missing = true;
        Assert.IsType<NotFoundResult>(await controller.Reprocess(9, new DateTime(2026, 10, 5), CancellationToken.None));
    }

    [Fact]
    public void TheAttendanceScreen_IsForEmployeesManagersAndHr()
    {
        string[] Controllers(params string[] roles) => NavigationMenu.For(new FakeActor(roles)).SelectMany(s => s.Items).Select(i => i.Controller).ToArray();

        Assert.Contains("Records", Controllers("Employee"));
        Assert.Contains("Records", Controllers("Manager"));
        Assert.Contains("Records", Controllers("HRAdministrator"));
        Assert.DoesNotContain("Records", Controllers("PayrollAdministrator", "IntegrationAdministrator"));
    }

    private static RecordsController Controller(FakeRecords records, string[] roles) =>
        new(records, new FakeActor(roles)) { TempData = new TempDataDictionary(new DefaultHttpContext(), new NullTempDataProvider()) };

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

    private sealed class FakeRecords : IRecordsApiClient
    {
        public string? Status { get; private set; }

        public bool OnlyWithExceptions { get; private set; }

        public DateTime? From { get; private set; }

        public int? EmployeeId { get; private set; }

        public bool Missing { get; set; }

        public ReprocessResponse Result { get; set; } = new();

        public int PageAsked { get; private set; }

        public int PageSizeAsked { get; private set; }

        public int Total { get; set; }

        public int ServerPage { get; set; } = 1;

        public Task<PagedResult<RecordDayListItem>> ListAsync(DateTime? from, DateTime? to, int? employeeId, string? status, bool onlyWithExceptions, int page, int pageSize, CancellationToken cancellationToken)
        {
            (From, Status, OnlyWithExceptions, EmployeeId) = (from, status, onlyWithExceptions, employeeId);
            (PageAsked, PageSizeAsked) = (page, pageSize);
            return Task.FromResult(new PagedResult<RecordDayListItem> { Total = Total, Page = ServerPage, PageSize = pageSize });
        }

        public Task<IReadOnlyList<EmployeeOption>> ListEmployeeOptionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EmployeeOption>>([new EmployeeOption { Id = 12, Name = "Ada Lovelace (E-1)" }]);

        public Task<RecordDayDetail?> GetDayAsync(int employeeId, DateTime date, CancellationToken cancellationToken) =>
            Task.FromResult<RecordDayDetail?>(Missing ? null : new RecordDayDetail { EmployeeId = employeeId, AttendanceDate = date });

        public Task<ReprocessResponse?> ReprocessAsync(int employeeId, DateTime date, CancellationToken cancellationToken) =>
            Task.FromResult<ReprocessResponse?>(Missing ? null : Result);
    }
}
