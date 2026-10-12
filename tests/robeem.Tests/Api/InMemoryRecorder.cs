using robeem.Api.Attendance.Jobs;
using robeem.DataModel;

namespace robeem.Tests.Api;

/// <summary>The history the scheduler wrote, in memory.</summary>
internal sealed class InMemoryRecorder : IJobRunRecorder
{
    private readonly object _lock = new();
    private readonly List<RecordedRun> _runs = [];

    public IReadOnlyList<RecordedRun> Runs
    {
        get
        {
            lock (_lock)
            {
                return _runs.ToList();
            }
        }
    }

    public Task<int?> BeginAsync(string tenantId, string jobName, JobTrigger trigger, JobRunStatus status, DateTime? scheduledFor, DateTime startedAt, DateTime? completedAt, string? summary, int? requestedById, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var id = _runs.Count + 1;
            _runs.Add(new RecordedRun(id, tenantId, jobName, trigger, status, scheduledFor, startedAt, completedAt, 0, summary, requestedById));
            return Task.FromResult<int?>(id);
        }
    }

    public Task CompleteAsync(string tenantId, int runId, JobRunStatus status, int workItems, string? summary, DateTime completedAt, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var index = _runs.FindIndex(r => r.Id == runId);
            _runs[index] = _runs[index] with { Status = status, WorkItems = workItems, Summary = summary, CompletedAt = completedAt };
        }

        return Task.CompletedTask;
    }

    public Task<int> FailInterruptedAsync(string tenantId, DateTime now, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var count = 0;
            for (var i = 0; i < _runs.Count; i++)
            {
                if (_runs[i].TenantId == tenantId && _runs[i].Status == JobRunStatus.Running)
                {
                    _runs[i] = _runs[i] with { Status = JobRunStatus.Failed, CompletedAt = now, Summary = "interrupted" };
                    count++;
                }
            }

            return Task.FromResult(count);
        }
    }

    public Task<DateTime?> LastScheduledAtAsync(string tenantId, string jobName, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var times = _runs.Where(r => r.TenantId == tenantId && r.JobName == jobName && r.Trigger == JobTrigger.Scheduled).Select(r => r.ScheduledFor ?? r.StartedAt).ToList();
            return Task.FromResult<DateTime?>(times.Count == 0 ? null : times.Max());
        }
    }
}
