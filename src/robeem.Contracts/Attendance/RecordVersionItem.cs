namespace robeem.Contracts.Attendance;

/// <summary>
/// One version of the calculated attendance of a shift. A recalculation that changes anything writes
/// a new version and retires the old one; nothing is rewritten.
/// </summary>
public sealed class RecordVersionItem
{
    public int Id { get; init; }

    public int Version { get; init; }

    public bool IsCurrent { get; init; }

    public int? SupersededByRecordId { get; init; }

    /// <summary>Gets the scheduled shift's title; null for work outside any scheduled shift.</summary>
    public string? ShiftTitle { get; init; }

    public DateTime? ScheduledStart { get; init; }

    public DateTime? ScheduledEnd { get; init; }

    public DateTime? ActualCheckIn { get; init; }

    /// <summary>Gets where the check-in came from: Normalized, Inferred or Corrected.</summary>
    public string? ActualCheckInSource { get; init; }

    public DateTime? ActualCheckOut { get; init; }

    public string? ActualCheckOutSource { get; init; }

    public DateTime? ActualBreakOut { get; init; }

    public string? ActualBreakOutSource { get; init; }

    public DateTime? ActualBreakIn { get; init; }

    public string? ActualBreakInSource { get; init; }

    public TimeSpan WorkingHours { get; init; }

    public TimeSpan BreakDuration { get; init; }

    public int LateMinutes { get; init; }

    public int EarlyDepartureMinutes { get; init; }

    public int OvertimeMinutes { get; init; }

    public string Status { get; init; } = string.Empty;

    /// <summary>Gets the review decisions on this version, oldest first.</summary>
    public IReadOnlyList<RecordReviewItem> Reviews { get; init; } = Array.Empty<RecordReviewItem>();
}
