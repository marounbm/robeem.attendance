using System.Collections.Concurrent;
using robeem.Application.Attendance;
using robeem.Application.Tenancy;
using robeem.DataModel;

namespace robeem.Api.Attendance.Jobs;

/// <summary>
/// Carries out the runs of the <see cref="IScheduledJob"/>s and records each one, per tenant:
/// a cron job's runs and a manual run are written as Running first and settled when they end; a tick
/// of an interval job is written only if it did something or failed, so a job that wakes every few
/// seconds does not fill the history with empty runs. The same failure repeated by every tick is
/// written once. One run of a job at a time: a scheduled run waits for a manual one, and a manual
/// one is refused while another run is going. Run a single Api instance (CLAUDE.md §2.8).
/// </summary>
public sealed class JobCoordinator : IJobCoordinator
{
    private const int MaxMissedRecorded = 10;

    private readonly IReadOnlyList<IScheduledJob> _jobs;
    private readonly ITenantRegistry _tenants;
    private readonly IJobRunRecorder _recorder;
    private readonly TimeProvider _clock;
    private readonly CancellationToken _stopping;
    private readonly ILogger<JobCoordinator> _logger;
    private readonly Dictionary<string, Slot> _slots;

    // The last failure of an interval job per tenant, so that a failure every tick is stored once.
    private readonly ConcurrentDictionary<(string TenantId, string JobName), string> _lastFailure = new();

    public JobCoordinator(IEnumerable<IScheduledJob> jobs, ITenantRegistry tenants, IJobRunRecorder recorder, TimeProvider clock, IHostApplicationLifetime lifetime, ILogger<JobCoordinator> logger)
    {
        _jobs = jobs.ToList();
        _tenants = tenants;
        _recorder = recorder;
        _clock = clock;
        _stopping = lifetime.ApplicationStopping;
        _logger = logger;
        _slots = _jobs.ToDictionary(j => j.Name, _ => new Slot(), StringComparer.Ordinal);
    }

    public IReadOnlyList<IScheduledJob> Jobs => _jobs;

    public JobState? GetState(string jobName) =>
        _slots.TryGetValue(jobName, out var slot) ? slot.Snapshot(jobName) : null;

    /// <summary>Runs the job for every tenant in turn, as the schedule's run due at <paramref name="scheduledFor"/>. Waits for a manual run of the job to end first.</summary>
    public async Task RunScheduledAsync(IScheduledJob job, DateTime? scheduledFor, CancellationToken cancellationToken)
    {
        var slot = _slots[job.Name];
        await slot.Gate.WaitAsync(cancellationToken);
        try
        {
            slot.CheckedAt = Now();
            foreach (var tenant in _tenants.Tenants)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await RunRecordedAsync(job, tenant.Id, JobTrigger.Scheduled, scheduledFor, null, null, cancellationToken);
            }
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    /// <summary>Tells the screen when the job is due next; null when it has no next run.</summary>
    public void SetNextRun(IScheduledJob job, DateTimeOffset? next) =>
        _slots[job.Name].NextRunAt = next?.DateTime;

    public async Task<ManualRunResult> StartManualAsync(string jobName, string tenantId, int? requestedById, CancellationToken cancellationToken)
    {
        var job = _jobs.FirstOrDefault(j => j.Name == jobName);
        if (job is null)
        {
            return new ManualRunResult(ManualRunOutcome.UnknownJob);
        }

        if (!job.Enabled)
        {
            return new ManualRunResult(ManualRunOutcome.Disabled);
        }

        if (_tenants.Find(tenantId) is null)
        {
            return new ManualRunResult(ManualRunOutcome.UnknownTenant);
        }

        var slot = _slots[job.Name];
        if (!await slot.Gate.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            return new ManualRunResult(ManualRunOutcome.AlreadyRunning);
        }

        int? runId;
        try
        {
            runId = await _recorder.BeginAsync(tenantId, job.Name, JobTrigger.Manual, JobRunStatus.Running, null, Now(), null, null, requestedById, cancellationToken);
        }
        catch
        {
            slot.Gate.Release();
            throw;
        }

        // The request returns now; the run goes on in the background, until the Api stops.
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await RunRecordedAsync(job, tenantId, JobTrigger.Manual, null, requestedById, runId, _stopping);
                }
                catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
                {
                    // Shutting down; the run was recorded as Cancelled.
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Manual run of job {Job} for tenant {TenantId} failed unexpectedly.", job.Name, tenantId);
                }
                finally
                {
                    slot.Gate.Release();
                }
            },
            CancellationToken.None);

        return new ManualRunResult(ManualRunOutcome.Started, runId);
    }

    /// <summary>
    /// What the Api missed while it was down: the runs left Running are settled as failed, and each
    /// cron occurrence that passed since the job's last scheduled run is recorded as Missed (at most
    /// a few per job). Interval jobs just carry on; there is no time they should have run at.
    /// </summary>
    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetLocalNow();
        foreach (var tenant in _tenants.Tenants)
        {
            try
            {
                var interrupted = await _recorder.FailInterruptedAsync(tenant.Id, now.DateTime, cancellationToken);
                if (interrupted > 0)
                {
                    _logger.LogWarning("{Count} job run(s) of tenant {TenantId} were interrupted by the last stop.", interrupted, tenant.Id);
                }

                foreach (var job in _jobs.Where(j => j.Enabled && j.Schedule.Cron is not null && CronSchedule.TryParse(j.Schedule.Cron, out _)))
                {
                    if (await _recorder.LastScheduledAtAsync(tenant.Id, job.Name, cancellationToken) is not { } last)
                    {
                        continue;
                    }

                    var reference = ToOffset(last);
                    for (var i = 0; i < MaxMissedRecorded; i++)
                    {
                        if (CronSchedule.Next(job.Schedule.Cron, reference, _clock.LocalTimeZone) is not { } due || due > now)
                        {
                            break;
                        }

                        await _recorder.BeginAsync(tenant.Id, job.Name, JobTrigger.Scheduled, JobRunStatus.Missed, due.DateTime, now.DateTime, now.DateTime, "Not run: the Api was not running at the scheduled time.", null, cancellationToken);
                        reference = due;
                    }
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Could not settle the job history of tenant {TenantId} at startup.", tenant.Id);
            }
        }
    }

    private async Task RunRecordedAsync(IScheduledJob job, string tenantId, JobTrigger trigger, DateTime? scheduledFor, int? requestedById, int? runId, CancellationToken cancellationToken)
    {
        var started = Now();
        var isCron = job.Schedule.Cron is not null;
        if (runId is null && (trigger == JobTrigger.Manual || isCron))
        {
            runId = await _recorder.BeginAsync(tenantId, job.Name, trigger, JobRunStatus.Running, scheduledFor, started, null, null, requestedById, cancellationToken);
        }

        JobRunResult? result = null;
        JobRunStatus status;
        string? summary;
        var cancelled = false;
        try
        {
            result = await job.RunForTenantAsync(tenantId, cancellationToken);
            status = result.Failed ? JobRunStatus.Failed : JobRunStatus.Succeeded;
            summary = result.Summary;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = JobRunStatus.Cancelled;
            summary = "Cancelled: the Api was shutting down.";
            cancelled = true;
        }
        catch (Exception ex)
        {
            status = JobRunStatus.Failed;
            summary = ex.GetBaseException().Message;
            _logger.LogError(ex, "Job {Job} failed for tenant {TenantId}.", job.Name, tenantId);
        }

        // A tick that did nothing is not stored; neither is a failure that repeats the last one.
        if (runId is null)
        {
            var quiet = status == JobRunStatus.Succeeded && result!.WorkItems == 0;
            var repeatedFailure = status == JobRunStatus.Failed && _lastFailure.TryGetValue((tenantId, job.Name), out var previous) && previous == summary;
            if (!quiet && !repeatedFailure)
            {
                runId = await _recorder.BeginAsync(tenantId, job.Name, trigger, JobRunStatus.Running, scheduledFor, started, null, null, requestedById, CancellationToken.None);
            }
        }

        if (status == JobRunStatus.Failed && !isCron && trigger == JobTrigger.Scheduled)
        {
            _lastFailure[(tenantId, job.Name)] = summary ?? string.Empty;
        }
        else if (status == JobRunStatus.Succeeded)
        {
            _lastFailure.TryRemove((tenantId, job.Name), out _);
        }

        if (runId is { } id)
        {
            await _recorder.CompleteAsync(tenantId, id, status, result?.WorkItems ?? 0, summary, Now(), CancellationToken.None);
        }

        if (cancelled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private DateTime Now() => _clock.GetLocalNow().DateTime;

    private DateTimeOffset ToOffset(DateTime local) =>
        new(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _clock.LocalTimeZone.GetUtcOffset(local));

    private sealed class Slot
    {
        private readonly object _lock = new();
        private DateTime? _nextRunAt;
        private DateTime? _checkedAt;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public DateTime? NextRunAt
        {
            get
            {
                lock (_lock)
                {
                    return _nextRunAt;
                }
            }

            set
            {
                lock (_lock)
                {
                    _nextRunAt = value;
                }
            }
        }

        public DateTime? CheckedAt
        {
            get
            {
                lock (_lock)
                {
                    return _checkedAt;
                }
            }

            set
            {
                lock (_lock)
                {
                    _checkedAt = value;
                }
            }
        }

        public JobState Snapshot(string name) => new(name, Gate.CurrentCount == 0, NextRunAt, CheckedAt);
    }
}
