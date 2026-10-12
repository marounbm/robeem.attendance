namespace robeem.Api.Attendance.Jobs;

/// <summary>
/// The scheduler's live state and the one place a job run is carried out and recorded, whether the
/// schedule starts it or somebody presses Run now. A job never runs twice at once.
/// </summary>
public interface IJobCoordinator
{
    /// <summary>Gets every job, switched on or not.</summary>
    IReadOnlyList<IScheduledJob> Jobs { get; }

    /// <summary>Returns what the scheduler knows about the job now; null for a name that is no job.</summary>
    JobState? GetState(string jobName);

    /// <summary>
    /// Starts a run of the job for one tenant, in the background, recorded as a manual run, and
    /// returns at once. Refused when the job is unknown, switched off, already running, or the
    /// tenant is not configured.
    /// </summary>
    Task<ManualRunResult> StartManualAsync(string jobName, string tenantId, int? requestedById, CancellationToken cancellationToken);
}
