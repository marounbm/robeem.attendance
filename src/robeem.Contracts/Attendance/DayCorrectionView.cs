namespace robeem.Contracts.Attendance;

/// <summary>Everything the Correct page shows about one of the employee's days: its shifts, every punch, and the exceptions to settle.</summary>
public sealed class DayCorrectionView
{
    public DateTime Date { get; init; }

    public int EmployeeId { get; init; }

    public string EmployeeName { get; init; } = string.Empty;

    /// <summary>Gets the day's scheduled shifts, for example "09:00–17:00, break 13:00–14:00".</summary>
    public IReadOnlyList<string> Shifts { get; init; } = Array.Empty<string>();

    /// <summary>Gets a value indicating whether a shift ends after midnight, so a punch may be added on the day after.</summary>
    public bool HasOvernightShift { get; init; }

    /// <summary>
    /// Gets the types of punch the day must have once corrected (CheckIn, BreakIn, BreakOut, CheckOut, in the order of the day): check-in and
    /// check-out always, the break pair when a shift has a break and the policy asks for it. A correction that leaves one out is refused.
    /// </summary>
    public IReadOnlyList<string> RequiredPunches { get; init; } = Array.Empty<string>();

    public IReadOnlyList<DayPunchItem> Punches { get; init; } = Array.Empty<DayPunchItem>();

    public IReadOnlyList<ExceptionListItem> Exceptions { get; init; } = Array.Empty<ExceptionListItem>();

    /// <summary>Gets a value indicating whether the employee can send a correction for the day now.</summary>
    public bool CanCorrect { get; init; }

    /// <summary>Gets why the day can't be corrected now; null when it can.</summary>
    public string? CannotCorrectReason { get; init; }
}
