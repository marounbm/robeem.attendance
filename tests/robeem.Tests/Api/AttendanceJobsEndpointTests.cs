using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using robeem.Api.Attendance.Jobs;
using robeem.Api.Security;
using robeem.Application.Attendance.Jobs;
using robeem.Application.Tenancy;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Tests.Api;

/// <summary>The jobs endpoints: who may look, who may start a job, and what the answers say.</summary>
public sealed class AttendanceJobsEndpointTests : IAsyncDisposable
{
    private readonly FakeCoordinator _coordinator = new();
    private readonly FakeLog _log = new();
    private readonly TestApi _api;

    public AttendanceJobsEndpointTests()
    {
        _api = TestApi.Start(services =>
        {
            services.AddSingleton<IJobCoordinator>(_coordinator);
            services.AddSingleton<IJobRunLog>(_log);
            services.AddSingleton<ITenantContext>(new FixedTenant("tenant-a"));
        });
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task TheJobs_AreListedWithTheirScheduleNextRunAndLastRun_ToHrAndIntegrationAdministrators()
    {
        foreach (var role in new[] { AttendanceRoles.HRAdministrator, AttendanceRoles.IntegrationAdministrator })
        {
            var response = await Send(HttpMethod.Get, "api/attendance/jobs", role);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var jobs = (await response.Content.ReadFromJsonAsync<List<JobStatusItem>>())!;
            var nightly = jobs.Single(j => j.Name == "nightly");
            Assert.Equal(("Cron 0 2 * * * (server local time)", true, new DateTime(2026, 10, 13, 2, 0, 0)), (nightly.Schedule, nightly.Enabled, nightly.NextRunAt));
            Assert.Equal("Succeeded", nightly.LastRun!.Status);

            // A job that is switched off has no next run, whatever the scheduler remembers.
            var off = jobs.Single(j => j.Name == "off");
            Assert.Equal((false, null), (off.Enabled, off.NextRunAt));
        }
    }

    [Theory]
    [InlineData(AttendanceRoles.Employee)]
    [InlineData(AttendanceRoles.Manager)]
    [InlineData(AttendanceRoles.PayrollAdministrator)]
    public async Task OtherRoles_CanNeitherListNorRun(string role)
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "api/attendance/jobs", role)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "api/attendance/jobs/runs", role)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "api/attendance/jobs/nightly/run", role)).StatusCode);
        Assert.Empty(_coordinator.Started);
    }

    [Fact]
    public async Task NobodySignedIn_Is401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "api/attendance/jobs", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Post, "api/attendance/jobs/nightly/run", null)).StatusCode);
    }

    [Fact]
    public async Task Hr_CanLookButNotStartAJob()
    {
        var response = await Send(HttpMethod.Post, "api/attendance/jobs/nightly/run", AttendanceRoles.HRAdministrator);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_coordinator.Started);
    }

    [Fact]
    public async Task AnIntegrationAdministrator_StartsAJobForTheCurrentTenant_AndGetsTheRunId()
    {
        var response = await Send(HttpMethod.Post, "api/attendance/jobs/nightly/run", AttendanceRoles.IntegrationAdministrator);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(42, (await response.Content.ReadFromJsonAsync<RunJobResponse>())!.RunId);
        Assert.Equal(("nightly", "tenant-a", (int?)1), Assert.Single(_coordinator.Started));
    }

    [Theory]
    [InlineData(ManualRunOutcome.UnknownJob, HttpStatusCode.NotFound)]
    [InlineData(ManualRunOutcome.UnknownTenant, HttpStatusCode.NotFound)]
    [InlineData(ManualRunOutcome.AlreadyRunning, HttpStatusCode.Conflict)]
    [InlineData(ManualRunOutcome.Disabled, HttpStatusCode.Conflict)]
    public async Task ARefusedStart_SaysWhy(ManualRunOutcome outcome, HttpStatusCode expected)
    {
        _coordinator.Outcome = outcome;

        var response = await Send(HttpMethod.Post, "api/attendance/jobs/nightly/run", AttendanceRoles.IntegrationAdministrator);

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.Conflict)
        {
            Assert.Contains("detail", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task TheHistory_IsFilteredByJobAndStatus_AndDefaultsToAHundred()
    {
        var response = await Send(HttpMethod.Get, "api/attendance/jobs/runs?jobName=nightly&status=Failed", AttendanceRoles.HRAdministrator);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("nightly", "Failed", 100), _log.LastQuery);
        Assert.Equal("nightly", (await response.Content.ReadFromJsonAsync<List<JobRunItem>>())!.Single().JobName);
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string url, string? role)
    {
        var request = new HttpRequestMessage(method, url);
        if (role is not null)
        {
            request.Headers.Add("X-Test-User", TestApi.As(1, role));
        }

        return _api.Client.SendAsync(request);
    }

    private sealed class FixedTenant(string id) : ITenantContext
    {
        public string? TenantId => id;
    }

    private sealed class FakeCoordinator : IJobCoordinator
    {
        public IReadOnlyList<IScheduledJob> Jobs { get; } =
        [
            new StubJob("nightly", JobSchedule.OnCron("0 2 * * *")),
            new StubJob("off", JobSchedule.Every(TimeSpan.FromSeconds(10))) { Enabled = false },
        ];

        public ManualRunOutcome Outcome { get; set; } = ManualRunOutcome.Started;

        public List<(string Job, string Tenant, int? RequestedById)> Started { get; } = [];

        public JobState? GetState(string jobName) => new(jobName, false, new DateTime(2026, 10, 13, 2, 0, 0), new DateTime(2026, 10, 12, 9, 0, 0));

        public Task<ManualRunResult> StartManualAsync(string jobName, string tenantId, int? requestedById, CancellationToken cancellationToken)
        {
            if (Outcome == ManualRunOutcome.Started)
            {
                Started.Add((jobName, tenantId, requestedById));
            }

            return Task.FromResult(new ManualRunResult(Outcome, Outcome == ManualRunOutcome.Started ? 42 : null));
        }
    }

    private sealed class FakeLog : IJobRunLog
    {
        public (string? Job, string? Status, int Take)? LastQuery { get; private set; }

        public Task<IReadOnlyList<JobRunItem>> ListAsync(string? jobName, string? status, int take, CancellationToken cancellationToken)
        {
            LastQuery = (jobName, status, take);
            return Task.FromResult<IReadOnlyList<JobRunItem>>([new JobRunItem { Id = 1, JobName = "nightly", Status = "Failed" }]);
        }

        public Task<IReadOnlyDictionary<string, JobRunItem>> LastRunsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, JobRunItem>>(new Dictionary<string, JobRunItem> { ["nightly"] = new() { Id = 1, JobName = "nightly", Status = "Succeeded" } });

        public Task<int> BeginAsync(string jobName, JobTrigger trigger, JobRunStatus status, DateTime? scheduledFor, DateTime startedAt, DateTime? completedAt, string? summary, int? requestedById, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task CompleteAsync(int runId, JobRunStatus status, int workItems, string? summary, DateTime completedAt, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DateTime?> LastScheduledAtAsync(string jobName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> FailInterruptedAsync(DateTime now, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> PruneAsync(DateTime olderThan, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
