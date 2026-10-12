namespace robeem.Contracts.Attendance;

/// <summary>A background job: what it does, when it runs, when it runs next, and how its last run for the current tenant ended.</summary>
public sealed class JobStatusItem
{
    public string Name { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the job is switched on in the Api's settings. A disabled job never runs and can't be run by hand.</summary>
    public bool Enabled { get; init; }

    /// <summary>Gets the schedule in words, e.g. "Every 10 seconds" or "Cron 0 2 * * * (server time)".</summary>
    public string Schedule { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether a run is in progress now (for any tenant).</summary>
    public bool IsRunning { get; init; }

    /// <summary>Gets the next scheduled run in server local time; null when the job is disabled or has no next run.</summary>
    public DateTime? NextRunAt { get; init; }

    /// <summary>Gets when the job last looked for work, in server local time, even if it found none and stored nothing.</summary>
    public DateTime? LastCheckedAt { get; init; }

    /// <summary>Gets the latest stored run for the current tenant; null when there is none.</summary>
    public JobRunItem? LastRun { get; init; }
}
