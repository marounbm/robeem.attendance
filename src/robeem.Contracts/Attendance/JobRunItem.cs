namespace robeem.Contracts.Attendance;

/// <summary>One run of a background job for the current tenant, as the history lists it.</summary>
public sealed class JobRunItem
{
    public int Id { get; init; }

    public string JobName { get; init; } = string.Empty;

    /// <summary>Gets what started the run: Scheduled or Manual.</summary>
    public string Trigger { get; init; } = string.Empty;

    /// <summary>Gets how the run stands: Running, Succeeded, Failed, Missed or Cancelled.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets when the run was due (a cron job's occurrence, including a missed one); null for a tick or a manual run.</summary>
    public DateTime? ScheduledFor { get; init; }

    public DateTime StartedAt { get; init; }

    public DateTime? CompletedAt { get; init; }

    public int WorkItems { get; init; }

    /// <summary>Gets what the run reports, or why it failed.</summary>
    public string? Summary { get; init; }

    /// <summary>Gets who started a manual run; empty otherwise.</summary>
    public string RequestedByName { get; init; } = string.Empty;
}
