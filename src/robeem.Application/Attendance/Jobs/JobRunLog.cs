using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance.Jobs;

public sealed class JobRunLog : IJobRunLog
{
    private const int PruneBatch = 1000;

    private readonly IRobeemDbContext _db;

    public JobRunLog(IRobeemDbContext db)
    {
        _db = db;
    }

    public async Task<int> BeginAsync(string jobName, JobTrigger trigger, JobRunStatus status, DateTime? scheduledFor, DateTime startedAt, DateTime? completedAt, string? summary, int? requestedById, CancellationToken cancellationToken)
    {
        // The token's employee id need not name a profile of this tenant; the run is recorded either way.
        var requester = requestedById is { } id && await _db.Profiles.AnyAsync(p => p.Id == id, cancellationToken) ? requestedById : null;

        var run = new AttendanceJobRun
        {
            JobName = jobName,
            Trigger = trigger,
            Status = status,
            ScheduledFor = scheduledFor,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Summary = Trim(summary),
            RequestedById = requester,
        };
        _db.AttendanceJobRuns.Add(run);
        await _db.SaveChangesAsync(cancellationToken);
        return run.Id;
    }

    public async Task CompleteAsync(int runId, JobRunStatus status, int workItems, string? summary, DateTime completedAt, CancellationToken cancellationToken)
    {
        var run = await _db.AttendanceJobRuns.SingleOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
        {
            return;
        }

        run.Status = status;
        run.WorkItems = workItems;
        run.Summary = Trim(summary);
        run.CompletedAt = completedAt;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<JobRunItem>> ListAsync(string? jobName, string? status, int take, CancellationToken cancellationToken)
    {
        var query = _db.AttendanceJobRuns.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(jobName))
        {
            query = query.Where(r => r.JobName == jobName);
        }

        if (Enum.TryParse<JobRunStatus>(status, true, out var parsed))
        {
            query = query.Where(r => r.Status == parsed);
        }

        return await Items(query.OrderByDescending(r => r.Id).Take(Math.Clamp(take, 1, 500))).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, JobRunItem>> LastRunsAsync(CancellationToken cancellationToken)
    {
        var lastIds = await _db.AttendanceJobRuns.AsNoTracking()
            .GroupBy(r => r.JobName)
            .Select(g => g.Max(r => r.Id))
            .ToListAsync(cancellationToken);
        var runs = await Items(_db.AttendanceJobRuns.AsNoTracking().Where(r => lastIds.Contains(r.Id))).ToListAsync(cancellationToken);
        return runs.ToDictionary(r => r.JobName, StringComparer.Ordinal);
    }

    public async Task<DateTime?> LastScheduledAtAsync(string jobName, CancellationToken cancellationToken)
    {
        var times = await _db.AttendanceJobRuns.AsNoTracking()
            .Where(r => r.JobName == jobName && r.Trigger == JobTrigger.Scheduled)
            .OrderByDescending(r => r.Id)
            .Take(20)
            .Select(r => r.ScheduledFor ?? r.StartedAt)
            .ToListAsync(cancellationToken);
        return times.Count == 0 ? null : times.Max();
    }

    public async Task<int> FailInterruptedAsync(DateTime now, CancellationToken cancellationToken)
    {
        var running = await _db.AttendanceJobRuns.Where(r => r.Status == JobRunStatus.Running).ToListAsync(cancellationToken);
        foreach (var run in running)
        {
            run.Status = JobRunStatus.Failed;
            run.CompletedAt = now;
            run.Summary = "Interrupted: the Api stopped or restarted while this run was in progress.";
        }

        if (running.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return running.Count;
    }

    public async Task<int> PruneAsync(DateTime olderThan, CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            var old = await _db.AttendanceJobRuns
                .Where(r => r.StartedAt < olderThan && r.Status != JobRunStatus.Running)
                .OrderBy(r => r.Id)
                .Take(PruneBatch)
                .ToListAsync(cancellationToken);
            if (old.Count == 0)
            {
                return total;
            }

            _db.AttendanceJobRuns.RemoveRange(old);
            await _db.SaveChangesAsync(cancellationToken);
            total += old.Count;
        }
    }

    private static string? Trim(string? summary) =>
        summary is null || summary.Length <= AttendanceJobRun.SummaryMaxLength ? summary : summary[..AttendanceJobRun.SummaryMaxLength];

    private static IQueryable<JobRunItem> Items(IQueryable<AttendanceJobRun> runs) => runs.Select(r => new JobRunItem
    {
        Id = r.Id,
        JobName = r.JobName,
        Trigger = r.Trigger.ToString(),
        Status = r.Status.ToString(),
        ScheduledFor = r.ScheduledFor,
        StartedAt = r.StartedAt,
        CompletedAt = r.CompletedAt,
        WorkItems = r.WorkItems,
        Summary = r.Summary,
        RequestedByName = r.RequestedBy == null ? string.Empty : r.RequestedBy.Title,
    });
}
