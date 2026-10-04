namespace robeem.Application.Attendance;

/// <summary>
/// One punch exactly as the vendor delivered it. The adapter only picks fields out; it interprets
/// nothing (that is normalization's job).
/// </summary>
public sealed record ExternalAttendanceTransaction(string RawPayload, string TransactionTimestampRaw)
{
    /// <summary>Gets the timestamp parsed from the vendor's format; null when it does not parse.</summary>
    public VendorTimestamp? Timestamp { get; init; }

    public string? ExternalTransactionId { get; init; }

    public string? ExternalEmployeeId { get; init; }

    public string? DeviceExternalId { get; init; }

    public string? TransactionTypeRaw { get; init; }

    public string? DirectionRaw { get; init; }
}
