using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.Web.ApiClients;
using robeem.Web.Controllers;
using robeem.Web.Models;

namespace robeem.Tests.Web;

/// <summary>The compensation screens: what is posted becomes one request, every page link keeps its filters, and a refusal is shown, not lost.</summary>
public sealed class CompensationWebTests
{
    private static readonly DateTime October = new(2026, 10, 1);

    [Fact]
    public async Task OnlyTheLinesGivenADecision_AreSent_WithTheNoteTrimmed_AndTheLeaveTypeOnlyWhenChosen()
    {
        var client = new FakeCompensation();
        var controller = WithTempData(new CompensationController(client));
        var form = new CompensationDecideForm
        {
            EmployeeId = 5,
            PeriodStart = October,
            Mode = CompensationIndexViewModel.Containing,
            Date = new DateTime(2026, 10, 8),
            Lines =
            [
                new CompensationLineChoice { Date = new DateTime(2026, 10, 5), LineKind = "Overtime", Decision = "Ignore", LeaveType = "Annual", Note = "  not needed  " },
                new CompensationLineChoice { Date = new DateTime(2026, 10, 6), LineKind = "Undertime", Decision = string.Empty },
                new CompensationLineChoice { Date = new DateTime(2026, 10, 7), LineKind = "Undertime", Decision = "PaidLeave", LeaveType = "Sick" },
            ],
        };

        var result = await controller.Decide(form, CancellationToken.None);

        var request = Assert.Single(client.Decisions);
        Assert.Equal((5, October), (request.EmployeeId, request.PeriodStart));
        Assert.Equal([("Overtime", "Ignore", "Annual", "not needed"), ("Undertime", "PaidLeave", "Sick", null)], request.Lines.Select(l => (l.LineKind, l.Decision, l.LeaveType, l.Note)));
        Assert.Null(request.UndecidedUndertime);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(("Employee", 5, "Containing", "2026-10-08"), (redirect.ActionName, redirect.RouteValues!["id"], redirect.RouteValues["mode"], redirect.RouteValues["date"]));
        Assert.Contains("recorded", (string)controller.TempData["StatusMessage"]!);
    }

    [Fact]
    public async Task ABulkChoice_IsSentAlone_AndNothingChosenIsNotSentAtAll()
    {
        var client = new FakeCompensation();
        var controller = WithTempData(new CompensationController(client));

        await controller.Decide(new CompensationDecideForm { EmployeeId = 5, PeriodStart = October, UndecidedUndertime = "Penalise" }, CancellationToken.None);
        await controller.Decide(new CompensationDecideForm { EmployeeId = 5, PeriodStart = October, Lines = [new CompensationLineChoice { Date = October, LineKind = "Overtime" }] }, CancellationToken.None);

        var request = Assert.Single(client.Decisions);
        Assert.Equal(("Penalise", 0), (request.UndecidedUndertime, request.Lines.Count));
        Assert.Contains("at least one line", (string)controller.TempData["ErrorMessage"]!);
    }

    [Fact]
    public async Task ARefusedDecision_ShowsWhy_AndReturnsToTheReport()
    {
        var client = new FakeCompensation { Result = ApiSaveResult.Invalid(new Dictionary<string, string> { [string.Empty] = "This overlaps another leave of the same employee." }) };
        var controller = WithTempData(new CompensationController(client));

        var result = await controller.Decide(new CompensationDecideForm { EmployeeId = 5, PeriodStart = October, UndecidedUndertime = "Ignore" }, CancellationToken.None);

        Assert.Equal("Employee", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("overlaps another leave", (string)controller.TempData["ErrorMessage"]!);
    }

    [Fact]
    public async Task TheOverview_TakesOnlyAPageSizeItOffers_AndEveryPageLinkKeepsTheSearchTheModeAndTheDate()
    {
        var client = new FakeCompensation { Total = 45 };
        var controller = new CompensationController(client);

        var view = Assert.IsType<ViewResult>(await controller.Index("  ada ", "containing", new DateTime(2026, 10, 8), 2, 37, CancellationToken.None));
        var model = Assert.IsType<CompensationIndexViewModel>(view.Model);

        Assert.Equal(("ada", "Containing", 20, 2), (model.Search, model.Mode, model.PageSize, model.Page));
        Assert.Equal(("ada", "Containing", (DateTime?)new DateTime(2026, 10, 8), 2, 20), client.Lists.Single());
        Assert.Equal(
            new Dictionary<string, string?> { ["pageSize"] = "20", ["mode"] = "Containing", ["search"] = "ada", ["date"] = "2026-10-08" },
            model.Pager.RouteValues);
        Assert.Equal(3, model.Pager.PageCount);
    }

    [Theory]
    [InlineData(null, "Latest")]
    [InlineData("anything else", "Latest")]
    [InlineData("Latest", "Latest")]
    [InlineData("last", "Last")]
    [InlineData("Last", "Last")]
    public async Task TheLatestPeriodWithAttendanceIsTheDefault_AndOnlyAChosenDateIsKept(string? asked, string expected)
    {
        var client = new FakeCompensation();
        var controller = new CompensationController(client);

        var view = Assert.IsType<ViewResult>(await controller.Index(null, asked, new DateTime(2026, 10, 8), 0, 0, CancellationToken.None));
        var model = Assert.IsType<CompensationIndexViewModel>(view.Model);

        Assert.Equal(expected, model.Mode);
        Assert.False(model.Pager.RouteValues.ContainsKey("date"));
        Assert.Equal((expected, 1), (client.Lists.Single().Mode, client.Lists.Single().Page));
    }

    [Fact]
    public async Task AnEmployeeThatDoesNotExist_IsNotFound()
    {
        var controller = new CompensationController(new FakeCompensation { Report = null });

        Assert.IsType<NotFoundResult>(await controller.Employee(99, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task ClosingAndReopening_ShowTheAnswer_AndReopeningNeedsAReasonBeforeAskingTheApi()
    {
        var client = new FakeCompensation();
        var controller = WithTempData(new CompensationController(client));

        await controller.Close(5, October, null, null, CancellationToken.None);
        Assert.Contains("closed", (string)controller.TempData["StatusMessage"]!);

        client.Result = ApiSaveResult.Invalid(new Dictionary<string, string> { [string.Empty] = "2 day(s) of the period are not ready for payroll." });
        await controller.Close(5, October, null, null, CancellationToken.None);
        Assert.Contains("not ready", (string)controller.TempData["ErrorMessage"]!);

        client.Result = ApiSaveResult.Success(null);
        await controller.Reopen(5, October, "  ", null, null, CancellationToken.None);
        Assert.Empty(client.Reopens);
        Assert.Contains("reason", (string)controller.TempData["ErrorMessage"]!);

        await controller.Reopen(5, October, " late fix ", null, null, CancellationToken.None);
        Assert.Equal("late fix", Assert.Single(client.Reopens).Reason);
    }

    [Fact]
    public async Task TheExport_IsOneCsvOfEveryEmployee_NotOnePage()
    {
        var client = new FakeCompensation
        {
            All =
            [
                Item(11, "E-10", "=Mallory"),
                Item(2, "E-2", "Ada, Lovelace"),
            ],
        };
        var controller = new CompensationController(client);

        var file = Assert.IsType<FileContentResult>(await controller.Export("e-", null, null, CancellationToken.None));

        Assert.Equal(("text/csv", "compensation.csv"), (file.ContentType, file.FileDownloadName));
        var text = global::System.Text.Encoding.UTF8.GetString(file.FileContents).TrimStart('﻿');
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(CompensationCsv.Header, lines[0]);

        // Shorter numbers first (E-2 before E-10), a name with a comma is quoted and a formula is defused.
        Assert.StartsWith("E-2,\"Ada, Lovelace\",Standard Week,2026-10-01,2026-10-31,no,1,9.00,8.00,1.00,0.50,1.00,0.50,0.50,0.50,0.00,0.00,20,1,2", lines[1]);
        Assert.StartsWith("E-10,'=Mallory,", lines[2]);
        Assert.Equal("e-", client.AllSearches.Single());
    }

    [Theory]
    [InlineData(83, "+1:23")]
    [InlineData(-45, "−0:45")]
    [InlineData(0, "0:00")]
    public void ABalanceIsWrittenWithItsSign(int minutes, string expected)
    {
        Assert.Equal(expected, DisplayFormats.SignedMinutes(minutes));
    }

    [Fact]
    public async Task ThePayCycles_ListAnEmployeeAPageAtATime_KeepTheSearch_AndSavingShowsTheAnswer()
    {
        var client = new FakeCycles { Total = 45 };
        var controller = WithTempData(new PayCyclesController(client));

        var view = Assert.IsType<ViewResult>(await controller.Index("  ada", 3, 50, CancellationToken.None));
        var model = Assert.IsType<PayCyclesIndexViewModel>(view.Model);

        Assert.Equal(("ada", 50), (model.Search, model.PageSize));
        Assert.Equal(new Dictionary<string, string?> { ["pageSize"] = "50", ["search"] = "ada" }, model.Pager.RouteValues);

        await controller.SaveDefault(new PayCycleInput { Frequency = "Monthly", StartDay = 26 }, CancellationToken.None);
        Assert.Contains("saved", (string)controller.TempData["StatusMessage"]!);

        client.Result = ApiSaveResult.Invalid(new Dictionary<string, string> { ["StartDay"] = "The month can start on day 1 to 28." });
        await controller.SaveDefault(new PayCycleInput { Frequency = "Monthly", StartDay = 31 }, CancellationToken.None);
        Assert.Contains("day 1 to 28", (string)controller.TempData["ErrorMessage"]!);
    }

    [Fact]
    public async Task AnEmployeesOwnCycle_IsSavedOrShownAgainWithTheErrors_AndRemovingGivesTheDefaultBack()
    {
        var client = new FakeCycles();
        var controller = WithTempData(new PayCyclesController(client));

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(await controller.Edit(3, new PayCycleInput { Frequency = "Weekly" }, CancellationToken.None)).ActionName);
        Assert.Equal((3, "Weekly"), (client.Saved.Single().EmployeeId, client.Saved.Single().Input.Frequency));

        client.Result = ApiSaveResult.Invalid(new Dictionary<string, string> { ["Frequency"] = "Choose one of: Weekly, EveryTwoWeeks, Monthly." });
        var again = Assert.IsType<ViewResult>(await controller.Edit(3, new PayCycleInput { Frequency = "Daily" }, CancellationToken.None));
        Assert.Contains("Frequency", controller.ModelState.Keys);
        Assert.Equal("Daily", Assert.IsType<PayCycleEditViewModel>(again.Model).Input.Frequency);

        client.Result = ApiSaveResult.Missing();
        Assert.IsType<NotFoundResult>(await controller.Edit(99, new PayCycleInput(), CancellationToken.None));

        client.Result = ApiSaveResult.Success(3);
        await controller.Remove(3, CancellationToken.None);
        Assert.Contains("default applies", (string)controller.TempData["StatusMessage"]!);
    }

    private static CompensationOverviewItem Item(int id, string number, string name) => new()
    {
        EmployeeId = id,
        EmployeeNumber = number,
        EmployeeName = name,
        ScheduleName = "Standard Week",
        PeriodStart = October,
        PeriodEnd = new DateTime(2026, 10, 31),
        NotReadyDays = 2,
        Summary = new CompensationSummary
        {
            PaidDays = 1,
            WorkedMinutes = 540,
            RegularMinutes = 480,
            OvertimeMinutes = 60,
            UndertimeMinutes = 30,
            OvertimeCountedMinutes = 60,
            UndertimeRemainingMinutes = 30,
            BalanceMinutes = 30,
            OvertimeLineMinutes = 30,
            PenaltyMinutes = 20,
            UndecidedLines = 1,
        },
    };

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

    private sealed class FakeCompensation : ICompensationApiClient
    {
        public ApiSaveResult Result { get; set; } = ApiSaveResult.Success(null);

        public CompensationReport? Report { get; set; } = new() { EmployeeId = 5 };

        public int Total { get; set; }

        public IReadOnlyList<CompensationOverviewItem> All { get; set; } = [];

        public List<CompensationDecisionRequest> Decisions { get; } = [];

        public List<CompensationReopenRequest> Reopens { get; } = [];

        public List<(string? Search, string Mode, DateTime? Date, int Page, int PageSize)> Lists { get; } = [];

        public List<string?> AllSearches { get; } = [];

        public Task<PagedResult<CompensationOverviewItem>> ListAsync(string? search, string mode, DateTime? date, int page, int pageSize, CancellationToken cancellationToken)
        {
            Lists.Add((search, mode, date, page, pageSize));
            return Task.FromResult(new PagedResult<CompensationOverviewItem> { Items = [], Total = Total, Page = page, PageSize = pageSize });
        }

        public Task<IReadOnlyList<CompensationOverviewItem>> ListAllAsync(string? search, string mode, DateTime? date, CancellationToken cancellationToken)
        {
            AllSearches.Add(search);
            return Task.FromResult(All);
        }

        public Task<CompensationReport?> GetReportAsync(int employeeId, string mode, DateTime? date, CancellationToken cancellationToken) => Task.FromResult(Report);

        public Task<ApiSaveResult> DecideAsync(CompensationDecisionRequest request, CancellationToken cancellationToken)
        {
            Decisions.Add(request);
            return Task.FromResult(Result);
        }

        public Task<ApiSaveResult> CloseAsync(CompensationCloseRequest request, CancellationToken cancellationToken) => Task.FromResult(Result);

        public Task<ApiSaveResult> ReopenAsync(CompensationReopenRequest request, CancellationToken cancellationToken)
        {
            Reopens.Add(request);
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeCycles : IPayCyclesApiClient
    {
        public ApiSaveResult Result { get; set; } = ApiSaveResult.Success(null);

        public int Total { get; set; }

        public List<(int EmployeeId, PayCycleInput Input)> Saved { get; } = [];

        public Task<PayCycleItem> GetDefaultAsync(CancellationToken cancellationToken) => Task.FromResult(new PayCycleItem { Description = "Calendar month" });

        public Task<ApiSaveResult> SaveDefaultAsync(PayCycleInput input, CancellationToken cancellationToken) => Task.FromResult(Result);

        public Task<PagedResult<EmployeePayCycleItem>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<EmployeePayCycleItem> { Items = [], Total = Total, Page = page, PageSize = pageSize });

        public Task<PayCycleInput?> GetEmployeeAsync(int employeeId, CancellationToken cancellationToken) => Task.FromResult<PayCycleInput?>(null);

        public Task<ApiSaveResult> SaveEmployeeAsync(int employeeId, PayCycleInput input, CancellationToken cancellationToken)
        {
            Saved.Add((employeeId, input));
            return Task.FromResult(Result);
        }

        public Task<ApiSaveResult> RemoveEmployeeAsync(int employeeId, CancellationToken cancellationToken) => Task.FromResult(Result);
    }
}
