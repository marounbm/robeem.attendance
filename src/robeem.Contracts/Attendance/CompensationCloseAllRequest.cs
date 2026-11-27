namespace robeem.Contracts.Attendance;

/// <summary>
/// Closes the pay period shown for every employee of the overview that is ready: the same search, mode and date as the overview, so what is
/// closed is what was on the screen. Each employee's own period is closed; the others are skipped with a reason.
/// </summary>
public sealed class CompensationCloseAllRequest
{
    /// <summary>Gets the employee number or name the overview was narrowed to; null for everyone.</summary>
    public string? Search { get; init; }

    /// <summary>Gets the period shown: Latest, Last or Containing.</summary>
    public string? Mode { get; init; }

    /// <summary>Gets the date whose period is shown, when <see cref="Mode"/> is Containing.</summary>
    public DateTime? Date { get; init; }
}
