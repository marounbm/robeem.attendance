namespace robeem.Contracts.Attendance;

/// <summary>One vendor-neutral punch, interpreted from a raw punch.</summary>
public sealed class NormalizedTransactionListItem
{
    public int Id { get; init; }

    public int RawId { get; init; }

    public int IntegrationId { get; init; }

    public string IntegrationName { get; init; } = string.Empty;

    /// <summary>Gets the employee's number; null while the vendor's employee id is unrecognized.</summary>
    public string? EmployeeNumber { get; init; }

    public string? EmployeeName { get; init; }

    public string? DeviceName { get; init; }

    /// <summary>Gets the date the punch counts towards.</summary>
    public DateTime AttendanceDate { get; init; }

    /// <summary>Gets the wall-clock time of the punch in <see cref="TimeZoneId"/>.</summary>
    public DateTime LocalTime { get; init; }

    public string TimeZoneId { get; init; } = string.Empty;

    /// <summary>Gets CheckIn, CheckOut, BreakOut, BreakIn, Unknown or Other.</summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>Gets the vendor's own label for the event, as received.</summary>
    public string? OriginalEventType { get; init; }

    /// <summary>Gets Device, MobileApp, WebPortal or Manual.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Gets how sure the type is: Certain, Inferred or Ambiguous.</summary>
    public string Confidence { get; init; } = string.Empty;

    /// <summary>Gets Pending, Processed or Excluded.</summary>
    public string ProcessingStatus { get; init; } = string.Empty;

    /// <summary>Gets Unique, DuplicateConfirmed or DuplicateCandidate.</summary>
    public string DuplicateStatus { get; init; } = string.Empty;

    /// <summary>Gets the normalized punch this one repeats, when it is a duplicate.</summary>
    public int? DuplicateOfId { get; init; }

    /// <summary>Gets None, FlaggedInvalidSequence, FlaggedUnrecognizedEmployee or FlaggedUnrecognizedDevice.</summary>
    public string ExceptionStatus { get; init; } = string.Empty;
}
