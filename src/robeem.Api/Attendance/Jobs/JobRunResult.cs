namespace robeem.Api.Attendance.Jobs;

/// <summary>
/// What one run of a job for one tenant did. A run with no <see cref="WorkItems"/> that did not
/// fail is a quiet tick, which an interval job does not store.
/// </summary>
/// <param name="WorkItems">How much it did: rows normalized, integrations synced, and so on.</param>
/// <param name="Summary">One line for the history; null when there is nothing to say.</param>
/// <param name="Failed">Whether part of the run failed although the job as a whole went on (one integration of several, say).</param>
public sealed record JobRunResult(int WorkItems, string? Summary = null, bool Failed = false)
{
    public static JobRunResult Idle { get; } = new(0);
}
