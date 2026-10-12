using robeem.DataModel;

namespace robeem.Api.Attendance.Jobs;

/// <summary>
/// Writes the run history of a tenant (<c>AttendanceJobRun</c>) from the scheduler, which is not
/// itself inside any tenant. Recording must never break a job: an implementation logs a failure to
/// record and carries on, and <see cref="BeginAsync"/> then returns null.
/// </summary>
public interface IJobRunRecorder
{
    Task<int?> BeginAsync(string tenantId, string jobName, JobTrigger trigger, JobRunStatus status, DateTime? scheduledFor, DateTime startedAt, DateTime? completedAt, string? summary, int? requestedById, CancellationToken cancellationToken);

    Task CompleteAsync(string tenantId, int runId, JobRunStatus status, int workItems, string? summary, DateTime completedAt, CancellationToken cancellationToken);

    /// <summary>Marks the runs of the tenant that were still Running when the Api stopped as Failed. Returns how many.</summary>
    Task<int> FailInterruptedAsync(string tenantId, DateTime now, CancellationToken cancellationToken);

    /// <summary>When a scheduled run of the job was last due or started in the tenant; null when it has none.</summary>
    Task<DateTime?> LastScheduledAtAsync(string tenantId, string jobName, CancellationToken cancellationToken);
}
