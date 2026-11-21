namespace robeem.Contracts.Attendance;

/// <summary>
/// The related exceptions of one employee's shift on one date, as one case a person settles: it waits for a correction, is accepted
/// as it is, or is closed. The exceptions it holds are listed with it.
/// </summary>
public sealed class ExceptionGroupListItem
{
    public int Id { get; init; }

    public int EmployeeId { get; init; }

    public string EmployeeName { get; init; } = string.Empty;

    public DateTime AttendanceDate { get; init; }

    /// <summary>Gets the shift's title; null for work outside any shift.</summary>
    public string? ShiftTitle { get; init; }

    /// <summary>Gets where the group stands: Open, PendingCorrection or Closed.</summary>
    public string Status { get; init; } = string.Empty;

    public DateTime DetectedAt { get; init; }

    public DateTime? ResolvedAt { get; init; }

    /// <summary>Gets how it was closed: AutoResolved, Correction, Accepted or ExcusedAbsence; null while it is not.</summary>
    public string? ResolutionMethod { get; init; }

    /// <summary>Gets the name of who accepted it; null unless it was accepted.</summary>
    public string? ResolvedByName { get; init; }

    /// <summary>Gets the reason it was accepted with; null if it was not.</summary>
    public string? ResolutionNote { get; init; }

    /// <summary>Gets the number of exceptions in the group that are not closed.</summary>
    public int ActiveCount { get; init; }

    /// <summary>Gets a value indicating whether a correction of the day is waiting for a decision: no one can correct or accept until it is decided.</summary>
    public bool IsWaiting { get; init; }

    /// <summary>
    /// Gets a value indicating whether the user can correct the day now: it is their own, the group is open, and no correction of the day is waiting.
    /// </summary>
    public bool CanCorrect { get; init; }

    /// <summary>
    /// Gets a value indicating whether the user can accept the whole group as it is: it is open, every exception in it can be accepted (a missing
    /// punch cannot), and the user is the employee's manager or HR.
    /// </summary>
    public bool CanAccept { get; init; }

    /// <summary>Gets the exceptions of the group, oldest first.</summary>
    public IReadOnlyList<ExceptionListItem> Exceptions { get; init; } = Array.Empty<ExceptionListItem>();
}
