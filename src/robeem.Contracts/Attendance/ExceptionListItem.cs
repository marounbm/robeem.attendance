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

    /// <summary>Gets a value indicating whether the shift ends after midnight, so a missing punch may fall on the day after <see cref="AttendanceDate"/>.</summary>
    public bool ShiftIsOvernight { get; init; }

    /// <summary>Gets the kind: MissingPunch or InvalidSequence.</summary>
    public string ExceptionType { get; init; } = string.Empty;

    /// <summary>Gets the punch that is absent (CheckIn, CheckOut, BreakOut or BreakIn); null for an invalid sequence.</summary>
    public string? MissingEventType { get; init; }

    /// <summary>Gets the type of the out-of-order punch of an invalid sequence (CheckIn, CheckOut, BreakOut or BreakIn); null for any other exception.</summary>
    public string? RelatedPunchType { get; init; }

    /// <summary>Gets when the out-of-order punch happened, as local wall-clock time; null for any other exception.</summary>
    public DateTime? RelatedPunchAt { get; init; }

    /// <summary>
    /// Gets the types the out-of-order punch can be changed to so that this exception goes away (event type names, e.g. CheckIn).
    /// Empty for any other exception, and when no other type would settle it.
    /// </summary>
    public IReadOnlyList<string> SuggestedEventTypes { get; init; } = Array.Empty<string>();

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

    /// <summary>
    /// Gets a value indicating whether the user can accept it now: an open break exception (long, late or early) of someone else
    /// whom the user manages, or anyone for HR.
    /// </summary>
    public bool CanAccept { get; init; }

    /// <summary>Gets the reason it was accepted with; null if it was not.</summary>
    public string? ResolutionNote { get; init; }
}
