using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using robeem.Contracts.Attendance;
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
        var handler = new StubHttpHandler(HttpStatusCode.OK, "[]");
        var client = new RecordsApiClient(handler.CreateClient());

        await client.ListAsync(null, null, null, null, false, 200, CancellationToken.None);
        await client.ListAsync(new DateTime(2026, 10, 1, 14, 30, 0), new DateTime(2026, 10, 31), 12, "PendingReview", true, 50, CancellationToken.None);

        var day = new StubHttpHandler(HttpStatusCode.OK, """{"employeeId":7,"employeeName":"Ada Lovelace","processed":true,"versions":[{"id":3,"version":2,"isCurrent":true,"workingHours":"08:00:00"}]}""");
        var detail = await new RecordsApiClient(day.CreateClient()).GetDayAsync(7, new DateTime(2026, 10, 5, 9, 0, 0), CancellationToken.None);

        Assert.Equal(
            ["/api/attendance/records?take=200", "/api/attendance/records?take=50&from=2026-10-01&to=2026-10-31&employeeId=12&status=PendingReview&onlyWithExceptions=true"],
            handler.Requests.Select(r => r.RequestUri!.PathAndQuery));
        Assert.Equal("/api/attendance/records/7/2026-10-05", day.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal(TimeSpan.FromHours(8), Assert.Single(detail!.Versions).WorkingHours);
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

        var any = (RecordsIndexViewModel)((ViewResult)await controller.Index("Bogus", null, null, false, CancellationToken.None)).Model!;
        var askedForAny = records.Status;
        var pending = (RecordsIndexViewModel)((ViewResult)await controller.Index("PendingReview", new DateTime(2026, 10, 1), null, true, CancellationToken.None)).Model!;

        Assert.Equal(("Any", null), (any.Status, askedForAny));
        Assert.Equal("PendingReview", pending.Status);
        Assert.Equal(("PendingReview", true, new DateTime(2026, 10, 1)), (records.Status, records.OnlyWithExceptions, records.From));
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

        public bool Missing { get; set; }

        public ReprocessResponse Result { get; set; } = new();

        public Task<IReadOnlyList<RecordDayListItem>> ListAsync(DateTime? from, DateTime? to, int? employeeId, string? status, bool onlyWithExceptions, int take, CancellationToken cancellationToken)
        {
            (From, Status, OnlyWithExceptions) = (from, status, onlyWithExceptions);
            return Task.FromResult<IReadOnlyList<RecordDayListItem>>([]);
        }

        public Task<RecordDayDetail?> GetDayAsync(int employeeId, DateTime date, CancellationToken cancellationToken) =>
            Task.FromResult<RecordDayDetail?>(Missing ? null : new RecordDayDetail { EmployeeId = employeeId, AttendanceDate = date });

        public Task<ReprocessResponse?> ReprocessAsync(int employeeId, DateTime date, CancellationToken cancellationToken) =>
            Task.FromResult<ReprocessResponse?>(Missing ? null : Result);
    }
}
