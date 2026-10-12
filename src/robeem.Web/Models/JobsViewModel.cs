using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>The jobs screen: each job with its schedule and next run, and the run history.</summary>
public sealed class JobsViewModel
{
    public static IReadOnlyList<string> Statuses { get; } = ["Any", "Running", "Succeeded", "Failed", "Missed", "Cancelled"];

    public IReadOnlyList<JobStatusItem> Jobs { get; init; } = [];

    public IReadOnlyList<JobRunItem> Runs { get; init; } = [];

    /// <summary>Gets the job whose history is shown, or null for every job.</summary>
    public string? JobName { get; init; }

    /// <summary>Gets the status shown, or <c>Any</c>.</summary>
    public string Status { get; init; } = "Any";

    /// <summary>Gets a value indicating whether the user may start a job (an integration administrator).</summary>
    public bool CanRun { get; init; }

    /// <summary>Gets a value indicating whether a run is in progress, so the screen refreshes itself.</summary>
    public bool AnyRunning => Jobs.Any(j => j.IsRunning) || Runs.Any(r => r.Status == "Running");
}
