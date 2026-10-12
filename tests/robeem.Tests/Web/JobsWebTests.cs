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

/// <summary>The jobs screen: its typed client, its controller, and who sees it in the menu.</summary>
public sealed class JobsWebTests
{
    [Fact]
    public async Task TheClient_ListsTheJobs_TheirRuns_AndStartsOne()
    {
        var jobs = new StubHttpHandler(HttpStatusCode.OK, """[{"name":"nightly","displayName":"Nightly","enabled":true,"schedule":"Cron 0 2 * * *","isRunning":false,"nextRunAt":"2026-10-13T02:00:00","lastRun":{"id":3,"jobName":"nightly","status":"Succeeded"}}]""");
        var listed = await new JobsApiClient(jobs.CreateClient()).ListAsync(CancellationToken.None);

        Assert.Equal("/api/attendance/jobs", jobs.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal(("nightly", "Succeeded", new DateTime(2026, 10, 13, 2, 0, 0)), (listed[0].Name, listed[0].LastRun!.Status, listed[0].NextRunAt));

        var runs = new StubHttpHandler(HttpStatusCode.OK, """[{"id":3,"jobName":"nightly","status":"Missed","startedAt":"2026-10-13T09:00:00"}]""");
        var history = await new JobsApiClient(runs.CreateClient()).ListRunsAsync("nightly", "Missed", 50, CancellationToken.None);

        Assert.Equal("/api/attendance/jobs/runs?take=50&jobName=nightly&status=Missed", runs.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal("Missed", history.Single().Status);

        var started = new StubHttpHandler(HttpStatusCode.Accepted, """{"runId":42}""");
        var result = await new JobsApiClient(started.CreateClient()).RunAsync("nightly", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, started.Requests.Single().Method);
        Assert.Equal("/api/attendance/jobs/nightly/run", started.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal((true, 42), (result.Succeeded, result.Value!.RunId));
    }

    [Fact]
    public async Task AJobThatCannotBeStarted_ComesBackAsTheApisReason()
    {
        var conflict = new StubHttpHandler(HttpStatusCode.Conflict, """{"title":"The job can't be started.","detail":"The job is running already; wait for it to end.","status":409}""", "application/problem+json");

        var result = await new JobsApiClient(conflict.CreateClient()).RunAsync("nightly", CancellationToken.None);

        Assert.Equal((false, "The job is running already; wait for it to end."), (result.Succeeded, result.MessageText));
    }

    [Fact]
    public async Task TheScreen_ShowsTheJobsAndHistory_AndOnlyAnIntegrationAdministratorMayRun()
    {
        var jobs = new FakeJobs();

        var asHr = (JobsViewModel)((ViewResult)await Controller(jobs, NavigationMenu.Hr).Index("nightly", "Failed", CancellationToken.None)).Model!;

        Assert.Equal((false, "Failed", "nightly"), (asHr.CanRun, asHr.Status, asHr.JobName));
        Assert.Equal(("nightly", "Failed"), jobs.LastRuns);
        Assert.Equal("nightly", asHr.Jobs.Single().Name);

        // An unknown status is "any"; the Api is asked for no status at all.
        var asIntegration = (JobsViewModel)((ViewResult)await Controller(jobs, NavigationMenu.Integration).Index(null, "nonsense", CancellationToken.None)).Model!;

        Assert.Equal((true, "Any"), (asIntegration.CanRun, asIntegration.Status));
        Assert.Equal((null, null), jobs.LastRuns);
    }

    [Fact]
    public async Task RunNow_TellsTheUserItStarted_OrWhyItDidNot()
    {
        var jobs = new FakeJobs();
        var controller = Controller(jobs, NavigationMenu.Integration);

        await controller.Run("nightly", CancellationToken.None);
        Assert.Equal("nightly", jobs.Started);
        Assert.Contains("started", (string)controller.TempData["StatusMessage"]!);

        jobs.Refusal = "The job is running already.";
        await controller.Run("nightly", CancellationToken.None);
        Assert.Equal("The job is running already.", (string)controller.TempData["ErrorMessage"]!);
    }

    [Fact]
    public void TheJobsScreen_IsInTheMenu_ForHrAndIntegrationAdministratorsOnly()
    {
        var item = NavigationMenu.Find("Jobs")!;

        Assert.Equal([NavigationMenu.Integration, NavigationMenu.Hr], item.Roles);
        Assert.Contains("Jobs", Controllers("HRAdministrator"));
        Assert.Contains("Jobs", Controllers("IntegrationAdministrator"));
        Assert.DoesNotContain("Jobs", Controllers("Employee", "Manager", "PayrollAdministrator"));
    }

    [Theory]
    [InlineData("Missed", "warning")]
    [InlineData("Running", "info")]
    [InlineData("Failed", "danger")]
    [InlineData("Succeeded", "success")]
    [InlineData("Cancelled", "secondary")]
    public void EveryRunStatus_HasItsColour(string status, string colour) =>
        Assert.Equal(colour, DisplayFormats.Badge(status));

    [Theory]
    [InlineData(0.2, "<1 s")]
    [InlineData(12.4, "12 s")]
    [InlineData(185, "3 min 05 s")]
    public void ARunsDuration_IsWrittenForSecondsAndMinutes(double seconds, string expected) =>
        Assert.Equal(expected, DisplayFormats.Duration(TimeSpan.FromSeconds(seconds)));

    private static IEnumerable<string> Controllers(params string[] roles) =>
        NavigationMenu.For(new FakeActor(roles)).SelectMany(s => s.Items).Select(i => i.Controller);

    private static JobsController Controller(FakeJobs jobs, params string[] roles) =>
        new(jobs, new FakeActor(roles)) { TempData = new TempDataDictionary(new DefaultHttpContext(), new NullTempDataProvider()) };

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

    private sealed class FakeJobs : IJobsApiClient
    {
        public (string? Job, string? Status) LastRuns { get; private set; }

        public string? Started { get; private set; }

        public string? Refusal { get; set; }

        public Task<IReadOnlyList<JobStatusItem>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobStatusItem>>([new JobStatusItem { Name = "nightly", DisplayName = "Nightly", Enabled = true }]);

        public Task<IReadOnlyList<JobRunItem>> ListRunsAsync(string? jobName, string? status, int take, CancellationToken cancellationToken)
        {
            LastRuns = (jobName, status);
            return Task.FromResult<IReadOnlyList<JobRunItem>>([]);
        }

        public Task<ApiCommandResult<RunJobResponse>> RunAsync(string jobName, CancellationToken cancellationToken)
        {
            Started = jobName;
            return Task.FromResult(Refusal is null ? ApiCommandResult<RunJobResponse>.Ok(new RunJobResponse { RunId = 1 }) : ApiCommandResult<RunJobResponse>.Refused([Refusal]));
        }
    }
}
