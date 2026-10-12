namespace robeem.Api.Attendance.Jobs;

/// <summary>
/// A background job run by <see cref="ScheduledJobsHostedService"/>. The host calls it once per
/// tenant; the job works inside that tenant's scope (CLAUDE.md §2.8) and leaves the work to an
/// Application service. The host records how each run ended, so a job just does its work and
/// reports it; a run that throws is a failed run, and the schedule goes on.
/// </summary>
public interface IScheduledJob
{
    string Name { get; }

    /// <summary>Gets the name the jobs screen shows.</summary>
    string DisplayName { get; }

    /// <summary>Gets a sentence on what the job does, for the jobs screen.</summary>
    string Description { get; }

    bool Enabled { get; }

    JobSchedule Schedule { get; }

    /// <summary>Runs the job for one tenant. Throws when the run fails as a whole; reports a partial failure through <see cref="JobRunResult.Failed"/>.</summary>
    Task<JobRunResult> RunForTenantAsync(string tenantId, CancellationToken cancellationToken);
}
