namespace robeem.Contracts.Attendance;

/// <summary>A punch the day received, as normalized: what the calculation had to work from. Never a biometric template.</summary>
public sealed class RecordPunchItem
{
    public int Id { get; init; }

    /// <summary>Gets the wall-clock time of the punch in its time zone.</summary>
    public DateTime LocalTime { get; init; }

    public string TimeZoneId { get; init; } = string.Empty;

    /// <summary>Gets the type: CheckIn, CheckOut, BreakOut, BreakIn, Unknown or another event.</summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>Gets the vendor's own label, as received.</summary>
    public string? OriginalEventType { get; init; }

    public string? DeviceName { get; init; }

    /// <summary>Gets how sure the type is: Certain (the vendor labeled it), Inferred or Ambiguous.</summary>
    public string Confidence { get; init; } = string.Empty;

    /// <summary>Gets whether it is the canonical punch (Unique) or a repeat (DuplicateConfirmed, DuplicateCandidate).</summary>
    public string DuplicateStatus { get; init; } = string.Empty;
}
