using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance.Jobs;

/// <summary>
/// The history of the background job runs of the current tenant (<see cref="AttendanceJobRun"/>).
/// The scheduler writes it; the jobs screen reads it. Times are server local wall-clock times.
/// </summary>
public interface IJobRunLog
{
    /// <summary>
    /// Adds a run and returns its id. A run that is still going has <paramref name="status"/>
    /// <see cref="JobRunStatus.Running"/> and no <paramref name="completedAt"/>; a missed one is added
    /// already complete. <paramref name="requestedById"/> is dropped when it names no profile of the tenant.
    /// </summary>
    Task<int> BeginAsync(string jobName, JobTrigger trigger, JobRunStatus status, DateTime? scheduledFor, DateTime startedAt, DateTime? completedAt, string? summary, int? requestedById, CancellationToken cancellationToken);

    /// <summary>Settles a run: how it ended, how much it did, what it reports. Unknown ids are ignored.</summary>
    Task CompleteAsync(int runId, JobRunStatus status, int workItems, string? summary, DateTime completedAt, CancellationToken cancellationToken);

    /// <summary>The runs, newest first, optionally of one job and/or one status. <paramref name="take"/> is clamped to 1..500.</summary>
    Task<IReadOnlyList<JobRunItem>> ListAsync(string? jobName, string? status, int take, CancellationToken cancellationToken);

    /// <summary>The latest stored run of every job that has one, by job name.</summary>
    Task<IReadOnlyDictionary<string, JobRunItem>> LastRunsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The latest time a scheduled run of the job was due or started, from its scheduled and missed
    /// runs only (a run started by hand does not stand in for a scheduled one); null when it has none.
    /// </summary>
    Task<DateTime?> LastScheduledAtAsync(string jobName, CancellationToken cancellationToken);

    /// <summary>Marks the runs still Running as Failed ("interrupted"): nothing is running when the Api has just started. Returns how many.</summary>
    Task<int> FailInterruptedAsync(DateTime now, CancellationToken cancellationToken);

    /// <summary>Deletes the runs that started before <paramref name="olderThan"/>, except those still Running. Returns how many.</summary>
    Task<int> PruneAsync(DateTime olderThan, CancellationToken cancellationToken);
}
