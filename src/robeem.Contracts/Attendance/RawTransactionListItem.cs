namespace robeem.Contracts.Attendance;

/// <summary>One raw punch exactly as the vendor sent it, with where it came from and what became of it.</summary>
public sealed class RawTransactionListItem
{
    public int Id { get; init; }

    public int IntegrationId { get; init; }

    public string IntegrationName { get; init; } = string.Empty;

    public int BatchId { get; init; }

    public string? ExternalTransactionId { get; init; }

    public string? ExternalEmployeeId { get; init; }

    /// <summary>
    /// Gets the employee the vendor's employee id is mapped to now, on the device that took the punch: the device of an upload, else
    /// the one the punch was resolved to, else the one with the file's device id. Null when the device is unknown or the id is not mapped on it.
    /// </summary>
    public int? EmployeeId { get; init; }

    public string? EmployeeNumber { get; init; }

    public string? EmployeeName { get; init; }

    public string? DeviceExternalId { get; init; }

    public string TimestampRaw { get; init; } = string.Empty;

    public string? TypeRaw { get; init; }

    public string? DirectionRaw { get; init; }

    /// <summary>Gets the status: Pending, Normalized, Failed or Skipped.</summary>
    public string Status { get; init; } = string.Empty;

    public string? ProcessingError { get; init; }

    /// <summary>Gets when the batch that stored the row started (local time).</summary>
    public DateTime ReceivedAt { get; init; }

    /// <summary>Gets the payload as stored: the vendor's JSON after the adapter's allowlist and the sanitizer. Never a biometric template.</summary>
    public string Payload { get; init; } = string.Empty;

    /// <summary>Gets the normalized punch this row became, or null while it is waiting, failed or was skipped.</summary>
    public int? NormalizedId { get; init; }
}
