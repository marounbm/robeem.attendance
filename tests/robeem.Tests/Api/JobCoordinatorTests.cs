using Microsoft.Extensions.Logging.Abstractions;
using robeem.Api.Attendance.Jobs;
using robeem.DataModel;

namespace robeem.Tests.Api;

/// <summary>
/// How job runs are carried out and recorded: what is stored and what is not, manual runs, one run
/// of a job at a time, and what is settled when the Api starts. The history is in memory
/// (<see cref="InMemoryRecorder"/>); the real one is tested in <c>JobRunLogTests</c>.
/// </summary>
public sealed class JobCoordinatorTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly DateTime Night = new(2026, 10, 12, 2, 0, 0);

    private readonly InMemoryRecorder _recorder = new();
    private readonly FixedTimeProvider _clock = new(new DateTime(2026, 10, 12, 9, 0, 0));
    private readonly FakeLifetime _lifetime = new();

    [Fact]
    public async Task ACronRun_IsWrittenAsRunning_AndSettledWithItsWork_OnePerTenant()
    {
        var job = new StubJob("nightly", JobSchedule.OnCron("0 2 * * *")) { Result = _ => new JobRunResult(4, "4 things") };
        var coordinator = Coordinator([job], "a", "b");

        await coordinator.RunScheduledAsync(job, Night, CancellationToken.None);

        Assert.Equal(["a", "b"], _recorder.Runs.Select(r => r.TenantId));
        Assert.All(_recorder.Runs, r => Assert.Equal((JobTrigger.Scheduled, JobRunStatus.Succeeded, 4, "4 things", (DateTime?)Night), (r.Trigger, r.Status, r.WorkItems, r.Summary, r.ScheduledFor)));
        Assert.All(_recorder.Runs, r => Assert.NotNull(r.CompletedAt));
    }

    [Fact]
    public async Task AQuietTickOfAnIntervalJob_IsNotStored_ButATickWithWorkIs()
    {
        var work = 0;
        var job = new StubJob("tick", JobSchedule.Every(TimeSpan.FromSeconds(10))) { Result = _ => new JobRunResult(work) };
        var coordinator = Coordinator([job], "a");

        await coordinator.RunScheduledAsync(job, null, CancellationToken.None);
        Assert.Empty(_recorder.Runs);
        Assert.NotNull(coordinator.GetState("tick")!.LastCheckedAt);

        work = 3;
        await coordinator.RunScheduledAsync(job, null, CancellationToken.None);
        var run = Assert.Single(_recorder.Runs);
        Assert.Equal((JobRunStatus.Succeeded, 3), (run.Status, run.WorkItems));
    }

    [Fact]
    public async Task AFailedRun_IsStoredWithItsReason_AndTheSameFailureEveryTickOnlyOnce()
    {
        var failing = true;
        var job = new StubJob("tick", JobSchedule.Every(TimeSpan.FromSeconds(10)))
        {
            Result = _ => failing ? throw new InvalidOperationException("database is down") : JobRunResult.Idle,
        };
        var coordinator = Coordinator([job], "a");

        await coordinator.RunScheduledAsync(job, null, CancellationToken.None);
        await coordinator.RunScheduledAsync(job, null, CancellationToken.None);
        await coordinator.RunScheduledAsync(job, null, CancellationToken.None);

        var run = Assert.Single(_recorder.Runs);
        Assert.Equal((JobRunStatus.Failed, "database is down"), (run.Status, run.Summary));

        // Once it works again, a new failure is news.
        failing = false;
        await coordinator.RunScheduledAsync(job, null, CancellationToken.None);
        failing = true;
        await coordinator.RunScheduledAsync(job, null, CancellationToken.None);
        Assert.Equal(2, _recorder.Runs.Count);
    }

    [Fact]
    public async Task APartialFailure_IsAFailedRun_ThatKeepsItsWorkCount()
    {
        var job = new StubJob("sync", JobSchedule.Every(TimeSpan.FromSeconds(15))) { Result = _ => new JobRunResult(2, "2 synced. Problems: integration 3", Failed: true) };

        await Coordinator([job], "a").RunScheduledAsync(job, null, CancellationToken.None);

        var run = Assert.Single(_recorder.Runs);
        Assert.Equal((JobRunStatus.Failed, 2), (run.Status, run.WorkItems));
    }

    [Fact]
    public async Task OneTenantsFailure_DoesNotStopTheNext()
    {
        var job = new StubJob("nightly", JobSchedule.OnCron("0 2 * * *")) { Result = tenant => tenant == "a" ? throw new InvalidOperationException("a is broken") : new JobRunResult(1) };

        await Coordinator([job], "a", "b").RunScheduledAsync(job, Night, CancellationToken.None);

        Assert.Equal([("a", JobRunStatus.Failed), ("b", JobRunStatus.Succeeded)], _recorder.Runs.Select(r => (r.TenantId, r.Status)));
    }

    [Fact]
    public async Task ARunCancelledByShutdown_IsRecordedAsCancelled_AndTheCancellationGoesOn()
    {
        using var cts = new CancellationTokenSource();
        var job = new StubJob("nightly", JobSchedule.OnCron("0 2 * * *"))
        {
            Body = async (_, token) =>
            {
                await cts.CancelAsync();
                token.ThrowIfCancellationRequested();
                await Task.CompletedTask;
            },
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Coordinator([job], "a").RunScheduledAsync(job, Night, cts.Token));

        Assert.Equal(JobRunStatus.Cancelled, Assert.Single(_recorder.Runs).Status);
    }

    [Fact]
    public async Task RunNow_StartsTheJobForThatTenantOnly_AndRecordsWhoAskedAndHowItEnded()
    {
        var job = new StubJob("normalize", JobSchedule.Every(TimeSpan.FromSeconds(10))) { Result = _ => new JobRunResult(0) };
        var coordinator = Coordinator([job], "a", "b");

        var started = await coordinator.StartManualAsync("normalize", "b", 7, CancellationToken.None);

        Assert.Equal(ManualRunOutcome.Started, started.Outcome);
        await WaitUntil(() => _recorder.Runs.Any(r => r.Status != JobRunStatus.Running));
        var run = Assert.Single(_recorder.Runs);
        Assert.Equal(("b", JobTrigger.Manual, JobRunStatus.Succeeded, (int?)7, started.RunId), (run.TenantId, run.Trigger, run.Status, run.RequestedById, run.Id));
        Assert.Equal(["b"], job.Tenants);

        // A manual run is stored even though it did nothing.
        Assert.NotNull(run.CompletedAt);
    }

    [Fact]
    public async Task RunNow_IsRefusedWhileTheJobRuns_AndWhileItIsUnknownDisabledOrTheTenantUnknown()
    {
        var gate = new TaskCompletionSource();
        var slow = new StubJob("slow", JobSchedule.Every(TimeSpan.FromSeconds(10))) { Body = async (_, _) => await gate.Task };
        var off = new StubJob("off", JobSchedule.Every(TimeSpan.FromSeconds(10))) { Enabled = false };
        var coordinator = Coordinator([slow, off], "a");

        Assert.Equal(ManualRunOutcome.Started, (await coordinator.StartManualAsync("slow", "a", null, CancellationToken.None)).Outcome);
        Assert.True(coordinator.GetState("slow")!.IsRunning);

        Assert.Equal(ManualRunOutcome.AlreadyRunning, (await coordinator.StartManualAsync("slow", "a", null, CancellationToken.None)).Outcome);
        Assert.Equal(ManualRunOutcome.UnknownJob, (await coordinator.StartManualAsync("nope", "a", null, CancellationToken.None)).Outcome);
        Assert.Equal(ManualRunOutcome.Disabled, (await coordinator.StartManualAsync("off", "a", null, CancellationToken.None)).Outcome);
        Assert.Equal(ManualRunOutcome.UnknownTenant, (await coordinator.StartManualAsync("slow", "zzz", null, CancellationToken.None)).Outcome);

        gate.SetResult();
        await WaitUntil(() => !coordinator.GetState("slow")!.IsRunning);
        Assert.Equal(ManualRunOutcome.Started, (await coordinator.StartManualAsync("slow", "a", null, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task AScheduledRun_WaitsForAManualOne_InsteadOfOverlappingIt()
    {
        var gate = new TaskCompletionSource();
        var job = new StubJob("normalize", JobSchedule.Every(TimeSpan.FromSeconds(10))) { Body = async (_, _) => await gate.Task, Result = _ => new JobRunResult(1) };
        var coordinator = Coordinator([job], "a");
        await coordinator.StartManualAsync("normalize", "a", null, CancellationToken.None);

        var scheduled = coordinator.RunScheduledAsync(job, null, CancellationToken.None);
        await Task.Delay(50);
        Assert.False(scheduled.IsCompleted);
        Assert.Equal(1, job.MaxConcurrent);

        gate.SetResult();
        await scheduled.WaitAsync(Patience);
        Assert.Equal(1, job.MaxConcurrent);
    }

    [Fact]
    public async Task AtStartup_RunsLeftRunning_AreSettled_AndEachMissedCronTimeIsRecordedOnce()
    {
        var job = new StubJob("nightly", JobSchedule.OnCron("0 2 * * *"));
        var coordinator = Coordinator([job], "a");
        await _recorder.BeginAsync("a", "nightly", JobTrigger.Scheduled, JobRunStatus.Succeeded, Night, Night, Night, null, null, CancellationToken.None);
        await _recorder.BeginAsync("a", "other", JobTrigger.Scheduled, JobRunStatus.Running, null, Night, null, null, null, CancellationToken.None);

        // The Api was down for the 2:00 of the 13th and the 14th; it is 9:00 on the 14th.
        _clock.Local = new DateTime(2026, 10, 14, 9, 0, 0);
        await coordinator.RecoverAsync(CancellationToken.None);

        Assert.Equal(JobRunStatus.Failed, _recorder.Runs.Single(r => r.JobName == "other").Status);
        var missed = _recorder.Runs.Where(r => r.Status == JobRunStatus.Missed).ToList();
        Assert.Equal([new DateTime(2026, 10, 13, 2, 0, 0), new DateTime(2026, 10, 14, 2, 0, 0)], missed.Select(r => r.ScheduledFor!.Value));
        Assert.All(missed, r => Assert.Contains("not running", r.Summary));

        // A second start finds nothing new to record.
        await coordinator.RecoverAsync(CancellationToken.None);
        Assert.Equal(2, _recorder.Runs.Count(r => r.Status == JobRunStatus.Missed));
    }

    [Fact]
    public async Task AtStartup_NothingIsMissed_ForAJobThatHasNeverRun_OrWhoseOnlyRunWasManual()
    {
        var job = new StubJob("nightly", JobSchedule.OnCron("0 2 * * *"));
        var coordinator = Coordinator([job], "a");
        _clock.Local = new DateTime(2026, 10, 14, 9, 0, 0);

        await coordinator.RecoverAsync(CancellationToken.None);
        await _recorder.BeginAsync("a", "nightly", JobTrigger.Manual, JobRunStatus.Succeeded, null, _clock.Local, _clock.Local, null, null, CancellationToken.None);
        await coordinator.RecoverAsync(CancellationToken.None);

        Assert.DoesNotContain(_recorder.Runs, r => r.Status == JobRunStatus.Missed);
    }

    [Fact]
    public async Task AnIntervalJob_HasNoMissedRuns()
    {
        var job = new StubJob("tick", JobSchedule.Every(TimeSpan.FromSeconds(10)));
        var coordinator = Coordinator([job], "a");
        await _recorder.BeginAsync("a", "tick", JobTrigger.Scheduled, JobRunStatus.Succeeded, null, Night, Night, null, null, CancellationToken.None);
        _clock.Local = Night.AddDays(5);

        await coordinator.RecoverAsync(CancellationToken.None);

        Assert.Single(_recorder.Runs);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Patience);
        while (!condition())
        {
            if (timeout.IsCancellationRequested)
            {
                Assert.Fail("The condition was not met in time.");
            }

            await Task.Delay(5);
        }
    }

    private JobCoordinator Coordinator(IEnumerable<StubJob> jobs, params string[] tenants) =>
        new(jobs, new FakeTenantRegistry(tenants), _recorder, _clock, _lifetime, NullLogger<JobCoordinator>.Instance);
}
