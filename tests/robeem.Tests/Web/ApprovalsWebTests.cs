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
        Assert.Equal(["exceptionId", "proposedValue", "justification"], body.RootElement.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task TheCorrectionsClient_AsksForTheQueueOfARole_AndPostsTheDecision()
    {
        var queue = new StubHttpHandler(HttpStatusCode.OK, "[]");
        var decision = new StubHttpHandler(HttpStatusCode.OK, """{"correctionId":5,"status":"Applied","exceptionStatus":"Closed"}""");

        await new CorrectionsApiClient(queue.CreateClient()).ListPendingAsync(CorrectionApprover.HR, CancellationToken.None);
        var result = await new CorrectionsApiClient(decision.CreateClient()).DecideAsync(5, new DecideCorrectionRequest { Approved = true, IsHrDecision = true }, CancellationToken.None);

        Assert.Equal("/api/attendance/corrections/pending?awaiting=HR", queue.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal("/api/attendance/corrections/5/decision", decision.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal("Applied", result.Value!.Status);
    }

    [Fact]
    public async Task TheReviewsClient_ListsAndDecides()
    {
        var list = new StubHttpHandler(HttpStatusCode.OK, """[{"recordId":3,"employeeName":"Ada Lovelace","reason":"Overtime","overtimeMinutes":83}]""");
        var decide = new StubHttpHandler(HttpStatusCode.OK, """{"decisionId":1,"recordId":3,"outcome":"Approved","recordStatus":"Approved"}""");

        var items = await new ReviewsApiClient(list.CreateClient()).ListPendingAsync(CancellationToken.None);
        var result = await new ReviewsApiClient(decide.CreateClient()).DecideAsync(3, new DecideReviewRequest { Approved = true }, CancellationToken.None);

        Assert.Equal("/api/attendance/reviews/pending", list.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal(83, Assert.Single(items).OvertimeMinutes);
        Assert.Equal("/api/attendance/reviews/3/decision", decide.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal("Approved", result.Value!.Outcome);
    }

    [Fact]
    public async Task AForbiddenAction_IsThrown_NotShownAsAMessage()
    {
        var client = new ReviewsApiClient(new StubHttpHandler(HttpStatusCode.Forbidden).CreateClient());

        await Assert.ThrowsAsync<ApiAccessDeniedException>(() => client.DecideAsync(3, new DecideReviewRequest(), CancellationToken.None));
    }

    [Theory]
    [InlineData(new[] { "Manager" }, null, "Manager")]
    [InlineData(new[] { "HRAdministrator" }, null, "HR")]
    [InlineData(new[] { "Manager", "HRAdministrator" }, "HR", "HR")]
    [InlineData(new[] { "Manager" }, "HR", "Manager")]
    [InlineData(new[] { "Manager", "HRAdministrator" }, null, "Manager")]
    public async Task TheCorrectionsQueue_OpensOnATheActorMayUse(string[] roles, string? asked, string expected)
    {
        var corrections = new FakeCorrections();
        var controller = new CorrectionsController(corrections, new FakeActor(roles));

        var result = await controller.Index(asked is null ? null : Enum.Parse<CorrectionApprover>(asked), CancellationToken.None);

        var model = Assert.IsType<CorrectionsQueueViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(expected, model.Awaiting.ToString());
        Assert.Equal([Enum.Parse<CorrectionApprover>(expected)], corrections.Lists);
    }

    [Fact]
    public async Task ADecision_ForTheHrTab_IsSentAsAnHrDecision_AndTheResultIsExplained()
    {
        var corrections = new FakeCorrections { Status = "Pending" };
        var controller = WithTempData(new CorrectionsController(corrections, new FakeActor(["HRAdministrator"])));

        await controller.Decide(5, approved: true, CorrectionApprover.HR, CancellationToken.None);
        corrections.Status = "Applied";
        await controller.Decide(6, approved: true, CorrectionApprover.Manager, CancellationToken.None);

        Assert.Equal([(5, true, true), (6, true, false)], corrections.Decisions);
        Assert.Contains("Approved: the punch was added", (string)controller.TempData["StatusMessage"]!);
    }

    [Fact]
    public async Task ARefusedDecision_ShowsTheApisMessages()
    {
        var corrections = new FakeCorrections { Refusal = ["Only the employee's manager can decide this correction."] };
        var controller = WithTempData(new CorrectionsController(corrections, new FakeActor(["Manager"])));

        var result = await controller.Decide(5, true, CorrectionApprover.Manager, CancellationToken.None);

        Assert.Equal("Only the employee's manager can decide this correction.", controller.TempData["ErrorMessage"]);
        Assert.Null(controller.TempData["StatusMessage"]);
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
    }

    [Theory]
    [InlineData(new[] { "Manager" }, false)]
    [InlineData(new[] { "HRAdministrator" }, true)]
    [InlineData(new[] { "Manager", "HRAdministrator" }, true)]
    public async Task AReview_IsDecidedAsHr_WhenTheActorIsHr(string[] roles, bool asHr)
    {
        var reviews = new FakeReviews();
        var controller = WithTempData(new ReviewsController(reviews, new FakeActor(roles)));

        await controller.Decide(3, approved: false, "Not authorised.", CancellationToken.None);

        Assert.Equal((3, false, "Not authorised.", asHr), Assert.Single(reviews.Decisions));
        Assert.Contains("Rejected", (string)controller.TempData["StatusMessage"]!);
    }

    [Fact]
    public async Task ExceptionsProposal_SendsWhatWasTyped_AndReportsTheOutcome()
    {
        var corrections = new FakeCorrections();
        var controller = WithTempData(new ExceptionsController(new FakeExceptions(), corrections));

        await controller.Propose(4, new DateTime(2026, 10, 5, 12, 0, 0), "Reader missed it", CancellationToken.None);
        corrections.Refusal = ["The exception is closed."];
        await controller.Propose(4, new DateTime(2026, 10, 5, 12, 0, 0), null, CancellationToken.None);

        Assert.Equal([(4, new DateTime(2026, 10, 5, 12, 0, 0), "Reader missed it"), (4, new DateTime(2026, 10, 5, 12, 0, 0), string.Empty)], corrections.Proposals);
        Assert.Equal("The exception is closed.", controller.TempData["ErrorMessage"]);
    }

    [Theory]
    [InlineData("Closed", "Closed")]
    [InlineData("nonsense", "Active")]
    [InlineData(null, "Active")]
    public async Task TheExceptionsStatus_FallsBackToActive_WhenItIsNotOneWeKnow(string? status, string expected)
    {
        var exceptions = new FakeExceptions();
        var controller = new ExceptionsController(exceptions, new FakeCorrections());

        var result = await controller.Index(status, null, null, CancellationToken.None);

        Assert.Equal(expected, Assert.IsType<ExceptionsIndexViewModel>(Assert.IsType<ViewResult>(result).Model).Status);
        Assert.Equal(expected, exceptions.Statuses.Single());
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
        public string Status { get; set; } = "Applied";

        public IReadOnlyList<string>? Refusal { get; set; }

        public List<CorrectionApprover> Lists { get; } = [];

        public List<(int Id, bool Approved, bool AsHr)> Decisions { get; } = [];

        public List<(int ExceptionId, DateTime Time, string Justification)> Proposals { get; } = [];

        public Task<ApiCommandResult<CreatedResponse>> ProposeAsync(ProposeCorrectionRequest request, CancellationToken cancellationToken)
        {
            Proposals.Add((request.ExceptionId, request.ProposedValue, request.Justification));
            return Task.FromResult(Refusal is null ? ApiCommandResult<CreatedResponse>.Ok(new CreatedResponse { Id = 1 }) : ApiCommandResult<CreatedResponse>.Refused(Refusal));
        }

        public Task<IReadOnlyList<CorrectionListItem>> ListPendingAsync(CorrectionApprover awaiting, CancellationToken cancellationToken)
        {
            Lists.Add(awaiting);
            return Task.FromResult<IReadOnlyList<CorrectionListItem>>([]);
        }

        public Task<ApiCommandResult<CorrectionDecisionResponse>> DecideAsync(int correctionId, DecideCorrectionRequest request, CancellationToken cancellationToken)
        {
            Decisions.Add((correctionId, request.Approved, request.IsHrDecision));
            return Task.FromResult(Refusal is null
                ? ApiCommandResult<CorrectionDecisionResponse>.Ok(new CorrectionDecisionResponse { CorrectionId = correctionId, Status = Status })
                : ApiCommandResult<CorrectionDecisionResponse>.Refused(Refusal));
        }
    }

    private sealed class FakeReviews : IReviewsApiClient
    {
        public List<(int RecordId, bool Approved, string? Reason, bool AsHr)> Decisions { get; } = [];

        public Task<IReadOnlyList<ReviewListItem>> ListPendingAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReviewListItem>>([]);

        public Task<ApiCommandResult<ReviewDecisionResponse>> DecideAsync(int recordId, DecideReviewRequest request, CancellationToken cancellationToken)
        {
            Decisions.Add((recordId, request.Approved, request.Reason, request.IsHrDecision));
            return Task.FromResult(ApiCommandResult<ReviewDecisionResponse>.Ok(new ReviewDecisionResponse { RecordId = recordId }));
        }
    }

    private sealed class FakeExceptions : IExceptionsApiClient
    {
        public List<string?> Statuses { get; } = [];

        public Task<IReadOnlyList<ExceptionListItem>> ListAsync(string? status, DateTime? from, DateTime? to, int? employeeId, int take, CancellationToken cancellationToken)
        {
            Statuses.Add(status);
            return Task.FromResult<IReadOnlyList<ExceptionListItem>>([]);
        }
    }
}
