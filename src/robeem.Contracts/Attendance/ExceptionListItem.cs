namespace robeem.Contracts.Attendance;

/// <summary>An attendance exception (a missing punch, or punches in the wrong order) as a screen shows it.</summary>
public sealed class ExceptionListItem
{
    public int Id { get; init; }

    public int EmployeeId { get; init; }

    public string EmployeeName { get; init; } = string.Empty;

    public DateTime AttendanceDate { get; init; }

    /// <summary>Gets the shift's title; null for work outside any shift.</summary>
    public string? ShiftTitle { get; init; }

    /// <summary>Gets the kind: MissingPunch or InvalidSequence.</summary>
    public string ExceptionType { get; init; } = string.Empty;

    /// <summary>Gets the punch that is absent (CheckIn, CheckOut, BreakOut or BreakIn); null for an invalid sequence.</summary>
    public string? MissingEventType { get; init; }

    /// <summary>Gets where it stands: Open, PendingCorrection or Closed.</summary>
    public string Status { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public DateTime DetectedAt { get; init; }

    public DateTime? ResolvedAt { get; init; }

    /// <summary>Gets how it was closed: AutoResolved or Correction; null while it is not.</summary>
    public string? ResolutionMethod { get; init; }

    /// <summary>
    /// Gets a value indicating whether the user can propose a correction now: it is their own,
    /// a missing punch, and open. Only the exception's employee can propose.
    /// </summary>
    public bool CanPropose { get; init; }
}
