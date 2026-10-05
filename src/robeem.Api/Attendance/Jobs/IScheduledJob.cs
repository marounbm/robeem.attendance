namespace robeem.Api.Attendance.Jobs;

/// <summary>
/// A background job run by <see cref="ScheduledJobsHostedService"/>. A job walks the tenants itself,
/// each inside its own tenant scope (CLAUDE.md §2.8), and leaves the work to an Application service.
/// A run that throws is logged and the schedule goes on.
/// </summary>
public interface IScheduledJob
{
    string Name { get; }

    bool Enabled { get; }

    JobSchedule Schedule { get; }

    Task RunAsync(CancellationToken cancellationToken);
}
