using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.Web.Actors;
using robeem.Web.ApiClients;
using robeem.Web.Controllers;
using robeem.Web.Models;

namespace robeem.Tests.Web;

/// <summary>The approvals screens (screens phase 1): the typed clients and the controllers' decisions.</summary>
public sealed class ApprovalsWebTests
{
    private const string ValidationProblem = """
        {"title":"One or more validation errors occurred.","status":400,
         "errors":{"exceptionId":["The exception is closed."],"proposedValue":["The time can't be in the future."]}}
        """;

    [Fact]
    public async Task TheExceptionsClient_SendsItsFiltersAsDates()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, """[{"id":4,"employeeId":1,"employeeName":"Ada Lovelace","status":"Open","canPropose":true}]""");
        var client = new ExceptionsApiClient(handler.CreateClient());

        var items = await client.ListAsync("Open", new DateTime(2026, 10, 1, 14, 30, 0), new DateTime(2026, 10, 31), 12, 200, CancellationToken.None);

        Assert.Equal("/api/attendance/exceptions?take=200&status=Open&from=2026-10-01&to=2026-10-31&employeeId=12", handler.Requests.Single().RequestUri!.PathAndQuery);
        Assert.True(Assert.Single(items).CanPropose);
    }

    [Fact]
    public async Task ProposingACorrection_ReturnsTheNewId_OrTheMessagesToShow()
    {
        var created = new CorrectionsApiClient(new StubHttpHandler(HttpStatusCode.Created, """{"id":9}""").CreateClient());
        var handler = new StubHttpHandler(HttpStatusCode.BadRequest, ValidationProblem, "application/problem+json");
        var refused = new CorrectionsApiClient(handler.CreateClient());
        var request = new ProposeCorrectionRequest { ExceptionId = 4, ProposedValue = new DateTime(2026, 10, 5, 12, 0, 0), Justification = "Reader missed it" };

        var ok = await created.ProposeAsync(request, CancellationToken.None);
        var no = await refused.ProposeAsync(request, CancellationToken.None);

        Assert.Equal((true, 9), (ok.Succeeded, ok.Value!.Id));
        Assert.False(no.Succeeded);
        Assert.Equal(["The exception is closed.", "The time can't be in the future."], no.Messages);
        Assert.Equal("The exception is closed. The time can't be in the future.", no.MessageText);

        // The body carries what the user typed and nothing about who they are: the Api takes that from the token.
        using var body = JsonDocument.Parse(handler.Bodies.Single()!);
        Assert.Equal(["exceptionId", "proposedValue", "newEventType", "justification"], body.RootElement.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task TheApprovalsClient_ListsEveryFilterAsAQuery_AndPostsTheDecision()
    {
        var list = new StubHttpHandler(
            HttpStatusCode.OK,
            """{"items":[{"id":3,"type":"Review","employeeName":"Ada Lovelace","awaitingStep":"Manager","review":{"recordId":8,"reason":"Overtime","overtimeMinutes":83}},{"id":4,"type":"Correction","employeeName":"Ada Lovelace","awaitingStep":"HR","correction":{"id":9,"edits":[{"kind":"SupplyPunch","correctedEventType":"CheckIn"}]}}],"total":45,"page":2,"pageSize":20}""");
        var decide = new StubHttpHandler(HttpStatusCode.OK, """{"approvalId":4,"type":"Correction","status":"Applied","exceptionStatus":"Closed"}""");

        var page = await new ApprovalsApiClient(list.CreateClient()).ListPendingAsync(7, new DateTime(2026, 4, 1), new DateTime(2026, 4, 30), "Review", "Overtime", 2, 20, CancellationToken.None);
        var result = await new ApprovalsApiClient(decide.CreateClient()).DecideAsync(4, new DecideApprovalRequest { Approved = true, IsHrDecision = true }, CancellationToken.None);

        Assert.Equal("/api/attendance/approvals/pending?page=2&pageSize=20&employeeId=7&from=2026-04-01&to=2026-04-30&type=Review&reason=Overtime", list.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal((45, 2, 3), (page.Total, page.Page, page.PageCount));
        Assert.Equal(["Review", "Correction"], page.Items.Select(i => i.Type));
        Assert.Equal(83, page.Items[0].Review!.OvertimeMinutes);
        Assert.Equal("SupplyPunch", Assert.Single(page.Items[1].Correction!.Edits).Kind);
        Assert.Equal("/api/attendance/approvals/4/decision", decide.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal(("Correction", "Applied"), (result.Value!.Type, result.Value.Status));
    }

    [Fact]
    public async Task AForbiddenAction_IsThrown_NotShownAsAMessage()
    {
        var client = new ApprovalsApiClient(new StubHttpHandler(HttpStatusCode.Forbidden).CreateClient());

        await Assert.ThrowsAsync<ApiAccessDeniedException>(() => client.DecideAsync(3, new DecideApprovalRequest(), CancellationToken.None));
    }

    [Theory]
    [InlineData("HR", true)]
    [InlineData("Manager", false)]
    [InlineData(null, false)]
    public async Task ADecision_IsSentInTheStepTheRowAskedFor(string? awaiting, bool asHr)
    {
        var approvals = new FakeApprovals();
        var controller = WithTempData(new ApprovalsController(approvals));

        await controller.Decide(5, approved: true, awaiting, null, null, null, null, null, null, 1, 20, CancellationToken.None);

        Assert.Equal((5, true, null, asHr), Assert.Single(approvals.Decisions));
    }

    [Fact]
    public async Task ADecision_IsExplained_ByWhatWasDecidedAndHowFarItGot()
    {
        var approvals = new FakeApprovals();
        var controller = WithTempData(new ApprovalsController(approvals));

        approvals.Answer = new ApprovalDecisionResponse { Type = "Correction", Status = "Applied" };
        await controller.Decide(5, true, "Manager", null, null, null, null, null, null, 1, 20, CancellationToken.None);
        Assert.Contains("Approved: the edits were made", (string)controller.TempData["StatusMessage"]!);

        approvals.Answer = new ApprovalDecisionResponse { Type = "Correction", Status = "Pending" };
        await controller.Decide(5, true, "Manager", null, null, null, null, null, null, 1, 20, CancellationToken.None);
        Assert.Contains("still waits for its next approver", (string)controller.TempData["StatusMessage"]!);

        approvals.Answer = new ApprovalDecisionResponse { Type = "Correction", Status = "Rejected" };
        await controller.Decide(5, false, "Manager", null, null, null, null, null, null, 1, 20, CancellationToken.None);
        Assert.Contains("Rejected: the employee can propose again", (string)controller.TempData["StatusMessage"]!);

        approvals.Answer = new ApprovalDecisionResponse { Type = "Review", Status = "Rejected" };
        await controller.Decide(6, false, "Manager", "Not authorised.", null, null, null, null, null, 1, 20, CancellationToken.None);
        Assert.Contains("Rejected. The reason is on record", (string)controller.TempData["StatusMessage"]!);
        Assert.Equal((6, false, "Not authorised."), (approvals.Decisions[^1].Id, approvals.Decisions[^1].Approved, approvals.Decisions[^1].Reason));
    }

    [Fact]
    public async Task ARefusedDecision_ShowsTheApisMessages()
    {
        var approvals = new FakeApprovals { Refusal = ["Only the employee's manager can decide this correction."] };
        var controller = WithTempData(new ApprovalsController(approvals));

        var result = await controller.Decide(5, true, "Manager", null, null, null, null, null, null, 1, 20, CancellationToken.None);

        Assert.Equal("Only the employee's manager can decide this correction.", controller.TempData["ErrorMessage"]);
        Assert.Null(controller.TempData["StatusMessage"]);
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
    }

    [Fact]
    public async Task TheQueue_IsPagedOnTheServer_DropsUnknownNamesAndPageSizes_AndKeepsEveryFilterOnEveryLink()
    {
        var approvals = new FakeApprovals { Total = 45 };
        var controller = WithTempData(new ApprovalsController(approvals));

        var shown = Assert.IsType<ViewResult>(await controller.Index(7, new DateTime(2026, 4, 1), new DateTime(2026, 4, 30), "Review", "Overtime", 2, 10, CancellationToken.None));
        var model = Assert.IsType<ApprovalsQueueViewModel>(shown.Model);
        await controller.Index(null, null, null, "Bogus", "Bogus", -1, 7, CancellationToken.None);

        // Asked for page 2 of 10 with the kind and the reason, then (unknown names, an unknown size, a page before the first) page 1 of the default 20.
        Assert.Equal([(7, "Review", "Overtime", 2, 10), ((int?)null, (string?)null, (string?)null, 1, 20)], approvals.Asks);
        Assert.Equal((7, "Review", "Overtime", 2, 10, 45), (model.EmployeeId, model.Type, model.Why, model.Page, model.PageSize, model.Total));
        Assert.Equal("Ada Lovelace (E-1)", model.Employees.Single().Name);
        Assert.Equal(
            new Dictionary<string, string?> { ["pageSize"] = "10", ["employeeId"] = "7", ["from"] = "2026-04-01", ["to"] = "2026-04-30", ["type"] = "Review", ["why"] = "Overtime", ["page"] = "3" },
            model.Pager.For(3));
        Assert.Equal(5, model.Pager.PageCount);
    }

    [Fact]
    public async Task ADecision_ReturnsToTheQueueAsItWas_WithItsFiltersAndPage()
    {
        var controller = WithTempData(new ApprovalsController(new FakeApprovals()));

        var back = Assert.IsType<RedirectToActionResult>(await controller.Decide(3, true, "Manager", null, 7, new DateTime(2026, 4, 1), null, "Correction", "Overtime", 2, 50, CancellationToken.None));

        Assert.Equal("Index", back.ActionName);
        Assert.Equal(
            (7, "2026-04-01", "Correction", "Overtime", 2, 50),
            (back.RouteValues!["employeeId"], back.RouteValues["from"], back.RouteValues["type"], back.RouteValues["why"], back.RouteValues["page"], back.RouteValues["pageSize"]));
    }

    [Fact]
    public void TheQueueReturnValues_CarryTheFiltersAndThePage_ForTheDecisionForm()
    {
        var model = new ApprovalsQueueViewModel { Page = 3, PageSize = 10, EmployeeId = 7, Type = "Review", Why = "IncompleteDay", From = new DateTime(2026, 4, 1) };

        Assert.Equal(
            new Dictionary<string, string?> { ["pageSize"] = "10", ["employeeId"] = "7", ["from"] = "2026-04-01", ["type"] = "Review", ["why"] = "IncompleteDay", ["page"] = "3" },
            model.ReturnValues);
        Assert.True(model.IsFiltered);
        Assert.False(new ApprovalsQueueViewModel().IsFiltered);
    }

    [Fact]
    public async Task TheCorrectPage_ShowsTheDay_WithAChoiceForEveryPunchThatCanBeEdited()
    {
        var corrections = new FakeCorrections { Day = SampleDay() };
        var controller = WithTempData(new ExceptionsController(new FakeExceptions(), corrections, new FakeActor(["Employee"])));

        var model = Assert.IsType<DayCorrectionViewModel>(Assert.IsType<ViewResult>(await controller.Correct(new DateTime(2026, 10, 5), false, CancellationToken.None)).Model);

        Assert.Equal(new DateTime(2026, 10, 5), model.Form.Date);
        Assert.False(model.Form.FromDay);
        Assert.Equal([10, 12], model.Form.Punches.Select(p => p.TransactionId));
        Assert.All(model.Form.Punches, p => Assert.True(string.IsNullOrEmpty(p.Action)));

        corrections.Day = null;
        Assert.IsType<NotFoundResult>(await controller.Correct(new DateTime(2026, 10, 9), false, CancellationToken.None));
    }

    [Fact]
    public async Task ACorrectionStartedFromTheDay_ReturnsToTheDay_WhenSent()
    {
        var corrections = new FakeCorrections { Day = SampleDay() };
        var controller = WithTempData(new ExceptionsController(new FakeExceptions(), corrections, new FakeActor(["Employee"])));

        var opened = Assert.IsType<DayCorrectionViewModel>(Assert.IsType<ViewResult>(await controller.Correct(new DateTime(2026, 10, 5), true, CancellationToken.None)).Model);
        var result = await controller.Correct(new DayCorrectionForm { Date = new DateTime(2026, 10, 5), FromDay = true }, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.True(opened.Form.FromDay);
        Assert.Equal(("Day", "Records", 1, "2026-10-05"), (redirect.ActionName, redirect.ControllerName, redirect.RouteValues!["employeeId"], redirect.RouteValues["date"]));
    }

    [Fact]
    public async Task AcceptingFromTheDay_ReturnsToTheDay()
    {
        var controller = WithTempData(new ExceptionsController(new FakeExceptions(), new FakeCorrections(), new FakeActor(["Manager"])));

        var result = await controller.Accept(9, "Covered the phones", CancellationToken.None, 4, new DateTime(2026, 10, 5));

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(("Day", "Records", 4, "2026-10-05"), (redirect.ActionName, redirect.ControllerName, redirect.RouteValues!["employeeId"], redirect.RouteValues["date"]));
    }

    [Fact]
    public async Task TheEditsOfTheDay_AreSentTogether_AsChangesIgnoresAndAdditions_AndEmptyRowsAreLeftOut()
    {
        var corrections = new FakeCorrections { Day = SampleDay() };
        var controller = WithTempData(new ExceptionsController(new FakeExceptions(), corrections, new FakeActor(["Employee"])));
        var form = new DayCorrectionForm
        {
            Date = new DateTime(2026, 10, 5, 14, 30, 0),
            Justification = "Came back from lunch",
            Punches =
            [
                new PunchChoice { TransactionId = 10, Action = "CheckIn" },
                new PunchChoice { TransactionId = 12, Action = "Ignore" },
                new PunchChoice { TransactionId = 14, Action = string.Empty },
            ],
            Added =
            [
                new AddedPunch { EventType = "CheckOut", Time = new TimeOnly(17, 5) },
                new AddedPunch { EventType = "BreakIn", Time = new TimeOnly(0, 45), AfterMidnight = true },
                new AddedPunch(),
            ],
        };

        var result = await controller.Correct(form, CancellationToken.None);

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        var sent = Assert.Single(corrections.DayRequests);
        Assert.Equal((new DateTime(2026, 10, 5), "Came back from lunch"), (sent.Date, sent.Justification));
        Assert.Equal(
            [
                ("Change", 10, "CheckIn", (DateTime?)null),
                ("Ignore", 12, null, null),
                ("Add", null, "CheckOut", new DateTime(2026, 10, 5, 17, 5, 0)),
                ("Add", null, "BreakIn", new DateTime(2026, 10, 6, 0, 45, 0)),
            ],
            sent.Edits.Select(e => (e.Action, e.TransactionId, e.EventType, e.At)));
        Assert.Contains("sent", (string)controller.TempData["StatusMessage"]!);
    }

    [Fact]
    public async Task ARefusedCorrection_ShowsThePageAgain_WithWhatWasTyped_AndWhyItWasRefused()
    {
        var corrections = new FakeCorrections { Day = SampleDay(), Refusal = ["Edit 1: that punch is not one of this day's that count."] };
        var controller = WithTempData(new ExceptionsController(new FakeExceptions(), corrections, new FakeActor(["Employee"])));
        var form = new DayCorrectionForm
        {
            Date = new DateTime(2026, 10, 5),
            Justification = "Because",
            Punches = [new PunchChoice { TransactionId = 12, Action = "Ignore" }, new PunchChoice { TransactionId = 10, Action = "CheckIn" }],
            Added = [new AddedPunch { EventType = "CheckOut", Time = new TimeOnly(17, 5) }],
        };

        var result = await controller.Correct(form, CancellationToken.None);

        var model = Assert.IsType<DayCorrectionViewModel>(Assert.IsType<ViewResult>(result).Model);

        // The choices are shown against the punches as they are now, in the order the page lists them, whatever order they were posted in.
        Assert.Equal(["CheckIn", "Ignore"], model.Form.Punches.Select(p => p.Action));
        Assert.Equal("Because", model.Form.Justification);
        Assert.Equal(new TimeOnly(17, 5), Assert.Single(model.Form.Added).Time);
        Assert.Contains("that punch is not one of this day's", controller.ModelState[string.Empty]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public void TheExceptionsGrid_HasOneRowPerDay_NewestFirst_AndOffersCorrectOnlyWhereItCan()
    {
        var model = new ExceptionsIndexViewModel
        {
            Items =
            [
                new ExceptionListItem { Id = 1, EmployeeId = 7, EmployeeName = "Ada Lovelace", AttendanceDate = new DateTime(2026, 10, 5), Status = "Open", CanCorrect = true, ShiftTitle = "Day" },
                new ExceptionListItem { Id = 2, EmployeeId = 7, EmployeeName = "Ada Lovelace", AttendanceDate = new DateTime(2026, 10, 5), Status = "Open", CanCorrect = true, ShiftTitle = "Day" },
                new ExceptionListItem { Id = 3, EmployeeId = 7, EmployeeName = "Ada Lovelace", AttendanceDate = new DateTime(2026, 10, 6), Status = "Open", CanCorrect = true },
                new ExceptionListItem { Id = 4, EmployeeId = 7, EmployeeName = "Ada Lovelace", AttendanceDate = new DateTime(2026, 10, 6), Status = "PendingCorrection" },
                new ExceptionListItem { Id = 5, EmployeeId = 8, EmployeeName = "Charles Babbage", AttendanceDate = new DateTime(2026, 10, 5), Status = "Open" },
                new ExceptionListItem { Id = 6, EmployeeId = 7, EmployeeName = "Ada Lovelace", AttendanceDate = new DateTime(2026, 10, 1), Status = "Closed" },
            ],
        };

        var days = model.Days;

        Assert.Equal([(new DateTime(2026, 10, 6), 7), (new DateTime(2026, 10, 5), 7), (new DateTime(2026, 10, 5), 8), (new DateTime(2026, 10, 1), 7)], days.Select(d => (d.Date, d.EmployeeId)));
        Assert.Equal([2, 2, 1, 1], days.Select(d => d.Items.Count));

        // 5 Oct (Ada): two open exceptions, hers: Correct. 6 Oct: a correction already waits, so no. Charles's day is not hers to correct.
        Assert.Equal([false, true, false, false], days.Select(d => d.CanCorrect));
        Assert.Equal(["Open", "Open", "Open", "Closed"], days.Select(d => d.Status));
        Assert.True(days[0].IsWaiting);
        Assert.Equal(["Day"], days[1].Shifts);
        Assert.Equal(["Unscheduled work"], days[2].Shifts);
    }

    [Theory]
    [InlineData("HRAdministrator", true)]
    [InlineData("Manager", false)]
    public async Task AcceptingABreakException_SendsTheReason_AndWhetherTheUserDecidesAsHr(string role, bool asHr)
    {
        var exceptions = new FakeExceptions();
        var controller = WithTempData(new ExceptionsController(exceptions, new FakeCorrections(), new FakeActor([role])));

        await controller.Accept(9, "Covered the phones", CancellationToken.None);
        await controller.Accept(9, null, CancellationToken.None);

        Assert.Equal([(9, "Covered the phones", asHr), (9, string.Empty, asHr)], exceptions.Accepted);
        Assert.Contains("was accepted", (string)controller.TempData["StatusMessage"]!);
    }

    [Fact]
    public async Task AcceptingIsRefused_ByTheApi_WithItsReason()
    {
        var exceptions = new FakeExceptions { Refusal = "A reason is required to accept an exception." };
        var controller = WithTempData(new ExceptionsController(exceptions, new FakeCorrections(), new FakeActor(["Manager"])));

        await controller.Accept(9, " ", CancellationToken.None);

        Assert.Equal("A reason is required to accept an exception.", controller.TempData["ErrorMessage"]);
    }

    [Theory]
    [InlineData("Closed", "Closed")]
    [InlineData("nonsense", "Active")]
    [InlineData(null, "Active")]
    public async Task TheExceptionsStatus_FallsBackToActive_WhenItIsNotOneWeKnow(string? status, string expected)
    {
        var exceptions = new FakeExceptions();
        var controller = new ExceptionsController(exceptions, new FakeCorrections(), new FakeActor(["Employee"]));

        var result = await controller.Index(status, null, null, null, CancellationToken.None);

        Assert.Equal(expected, Assert.IsType<ExceptionsIndexViewModel>(Assert.IsType<ViewResult>(result).Model).Status);
        Assert.Equal(expected, exceptions.Statuses.Single());
    }

    [Fact]
    public async Task TheExceptionsList_CanBeNarrowedToOneEmployee_AndOffersTheEmployeesTheUserMaySee()
    {
        var exceptions = new FakeExceptions();
        exceptions.Employees.AddRange([new EmployeeOption { Id = 5, Name = "Hiba Rahme (21001)" }, new EmployeeOption { Id = 6, Name = "Yara Jabbour (22002)" }]);
        var controller = new ExceptionsController(exceptions, new FakeCorrections(), new FakeActor(["HRAdministrator"]));

        var narrowed = Assert.IsType<ExceptionsIndexViewModel>(Assert.IsType<ViewResult>(await controller.Index("Open", null, null, 6, CancellationToken.None)).Model);
        var everyone = Assert.IsType<ExceptionsIndexViewModel>(Assert.IsType<ViewResult>(await controller.Index("Open", null, null, null, CancellationToken.None)).Model);

        Assert.Equal([6, null], exceptions.EmployeeIds);
        Assert.Equal(6, narrowed.EmployeeId);
        Assert.Null(everyone.EmployeeId);
        Assert.Equal([5, 6], narrowed.Employees.Select(e => e.Id));
    }

    [Fact]
    public async Task TheClient_AsksForTheEmployeesOfTheFilter_FromTheRecordsOptions()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, """[{"id":5,"name":"Hiba Rahme (21001)"}]""");

        var options = await new ExceptionsApiClient(handler.CreateClient()).ListEmployeeOptionsAsync(CancellationToken.None);

        Assert.Equal("/api/attendance/records/employee-options", handler.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal("Hiba Rahme (21001)", options.Single().Name);
    }

    [Theory]
    [InlineData(83, "1:23")]
    [InlineData(0, "0:00")]
    [InlineData(600, "10:00")]
    public void MinutesAreWrittenAsHoursAndMinutes(int minutes, string expected)
    {
        Assert.Equal(expected, DisplayFormats.Minutes(minutes));
    }

    [Theory]
    [InlineData("PendingCorrection", "Pending correction")]
    [InlineData("UnscheduledWork", "Unscheduled work")]
    [InlineData("Open", "Open")]
    [InlineData(null, "—")]
    public void NamesAreSplitIntoWords(string? name, string expected)
    {
        Assert.Equal(expected, DisplayFormats.Words(name));
    }

    [Theory]
    [InlineData("PendingReview", 3, "Open exceptions", "info", true)]
    [InlineData("PendingReview", 0, "Pending review", "warning", false)]
    [InlineData("Approved", 0, "Approved", "success", false)]
    [InlineData("Rejected", 2, "Rejected", "danger", false)]
    [InlineData("Calculated", 1, "Calculated", "secondary", false)]
    public void ADayThatWaitsForItsExceptions_IsNotCalledPendingReview_BecauseApprovalsDoesNotListIt(string status, int exceptions, string label, string badge, bool hasHint)
    {
        Assert.Equal(label, DisplayFormats.DayStatus(status, exceptions));
        Assert.Equal(badge, DisplayFormats.DayStatusBadge(status, exceptions));
        Assert.Equal(hasHint, DisplayFormats.DayStatusHint(status, exceptions) is not null);
    }

    [Theory]
    [InlineData("PendingReview", 3)]
    [InlineData("Approved", 0)]
    [InlineData("Calculated", 1)]
    public void ADayWithACorrectionWaitingForTheApprover_IsPendingApproval_WhateverElseIsOpenOnIt(string status, int exceptions)
    {
        Assert.Equal("Pending approval", DisplayFormats.DayStatus(status, exceptions, pendingApproval: true));
        Assert.Equal("warning", DisplayFormats.DayStatusBadge(status, exceptions, pendingApproval: true));
        Assert.Contains("approver's decision", DisplayFormats.DayStatusHint(status, exceptions, pendingApproval: true));
    }

    private static DayCorrectionView SampleDay() => new()
    {
        Date = new DateTime(2026, 10, 5),
        EmployeeId = 7,
        EmployeeName = "Ada Lovelace",
        CanCorrect = true,
        Punches =
        [
            new DayPunchItem { TransactionId = 10, Type = "CheckOut", At = new DateTime(2026, 10, 5, 13, 57, 0), Editable = true, Problem = "A check-out follows a check-out.", SuggestedTypes = ["CheckIn"] },
            new DayPunchItem { TransactionId = 11, Type = "CheckOut", At = new DateTime(2026, 10, 5, 13, 57, 20), State = "Duplicate" },
            new DayPunchItem { TransactionId = 12, Type = "CheckIn", At = new DateTime(2026, 10, 5, 15, 36, 0), Editable = true },
            new DayPunchItem { Type = "CheckOut", At = new DateTime(2026, 10, 5, 18, 0, 0), Source = "Corrected", State = "Added" },
        ],
    };

    private static T WithTempData<T>(T controller)
        where T : Controller
    {
        controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(new Microsoft.AspNetCore.Http.DefaultHttpContext(), new NullTempDataProvider());
        return controller;
    }

    private sealed class NullTempDataProvider : Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(Microsoft.AspNetCore.Http.HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(Microsoft.AspNetCore.Http.HttpContext context, IDictionary<string, object> values)
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

    private sealed class FakeCorrections : ICorrectionsApiClient
    {
        public IReadOnlyList<string>? Refusal { get; set; }

        public List<(int ExceptionId, DateTime Time, string Justification)> Proposals { get; } = [];

        public List<DayCorrectionRequest> DayRequests { get; } = [];

        public DayCorrectionView? Day { get; set; }

        public Task<DayCorrectionView?> GetDayAsync(DateTime date, CancellationToken cancellationToken) => Task.FromResult(Day);

        public Task<ApiCommandResult<CreatedResponse>> ProposeDayAsync(DayCorrectionRequest request, CancellationToken cancellationToken)
        {
            DayRequests.Add(request);
            return Task.FromResult(Refusal is null ? ApiCommandResult<CreatedResponse>.Ok(new CreatedResponse { Id = 1 }) : ApiCommandResult<CreatedResponse>.Refused(Refusal));
        }

        public Task<ApiCommandResult<CreatedResponse>> ProposeAsync(ProposeCorrectionRequest request, CancellationToken cancellationToken)
        {
            Proposals.Add((request.ExceptionId, request.ProposedValue, request.Justification));
            return Task.FromResult(Refusal is null ? ApiCommandResult<CreatedResponse>.Ok(new CreatedResponse { Id = 1 }) : ApiCommandResult<CreatedResponse>.Refused(Refusal));
        }
    }

    private sealed class FakeApprovals : IApprovalsApiClient
    {
        public IReadOnlyList<string>? Refusal { get; set; }

        public ApprovalDecisionResponse Answer { get; set; } = new() { Type = "Correction", Status = "Applied" };

        public List<(int Id, bool Approved, string? Reason, bool AsHr)> Decisions { get; } = [];

        public List<(int? EmployeeId, string? Type, string? Reason, int Page, int PageSize)> Asks { get; } = [];

        public int Total { get; set; }

        public Task<PagedResult<ApprovalListItem>> ListPendingAsync(int? employeeId, DateTime? from, DateTime? to, string? type, string? reason, int page, int pageSize, CancellationToken cancellationToken)
        {
            Asks.Add((employeeId, type, reason, page, pageSize));
            return Task.FromResult(new PagedResult<ApprovalListItem> { Total = Total, Page = page, PageSize = pageSize });
        }

        public Task<IReadOnlyList<EmployeeOption>> ListEmployeeOptionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EmployeeOption>>([new EmployeeOption { Id = 7, Name = "Ada Lovelace (E-1)" }]);

        public Task<ApiCommandResult<ApprovalDecisionResponse>> DecideAsync(int approvalId, DecideApprovalRequest request, CancellationToken cancellationToken)
        {
            Decisions.Add((approvalId, request.Approved, request.Reason, request.IsHrDecision));
            return Task.FromResult(Refusal is null
                ? ApiCommandResult<ApprovalDecisionResponse>.Ok(Answer)
                : ApiCommandResult<ApprovalDecisionResponse>.Refused(Refusal));
        }
    }

    private sealed class FakeExceptions : IExceptionsApiClient
    {
        public List<string?> Statuses { get; } = [];

        public List<int?> EmployeeIds { get; } = [];

        public List<EmployeeOption> Employees { get; } = [];

        public List<(int ExceptionId, string Reason, bool IsHr)> Accepted { get; } = [];

        public string? Refusal { get; set; }

        public Task<IReadOnlyList<ExceptionListItem>> ListAsync(string? status, DateTime? from, DateTime? to, int? employeeId, int take, CancellationToken cancellationToken)
        {
            Statuses.Add(status);
            EmployeeIds.Add(employeeId);
            return Task.FromResult<IReadOnlyList<ExceptionListItem>>([]);
        }

        public Task<IReadOnlyList<EmployeeOption>> ListEmployeeOptionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EmployeeOption>>(Employees);

        public Task<ApiSaveResult> AcceptAsync(int exceptionId, AcceptExceptionRequest request, CancellationToken cancellationToken)
        {
            Accepted.Add((exceptionId, request.Reason, request.IsHrDecision));
            return Task.FromResult(Refusal is null
                ? ApiSaveResult.Success(exceptionId)
                : ApiSaveResult.Invalid(new Dictionary<string, string> { [string.Empty] = Refusal }));
        }
    }
}
