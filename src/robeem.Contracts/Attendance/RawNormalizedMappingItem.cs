namespace robeem.Contracts.Attendance;

/// <summary>
/// One raw punch beside the normalized punch it produced. The normalized side is empty while the
/// raw row is still waiting (Pending) or when it failed or was skipped: <see cref="ProcessingError"/> says why.
/// </summary>
public sealed class RawNormalizedMappingItem
{
    public string ProviderKey { get; init; } = string.Empty;

    public int IntegrationId { get; init; }

    public string IntegrationName { get; init; } = string.Empty;

    public int BatchId { get; init; }

    // Raw side: exactly what the vendor sent.
    public int RawId { get; init; }

    public string? ExternalTransactionId { get; init; }

    public string? ExternalEmployeeId { get; init; }

    public string? DeviceExternalId { get; init; }

    public string TimestampRaw { get; init; } = string.Empty;

    public string? TypeRaw { get; init; }

    public string? DirectionRaw { get; init; }

    /// <summary>Gets the raw row's status: Pending, Normalized, Failed or Skipped.</summary>
    public string RawStatus { get; init; } = string.Empty;

    public string? ProcessingError { get; init; }

    /// <summary>Gets the payload as stored: the vendor's JSON after the adapter's allowlist and the sanitizer. Never a biometric template.</summary>
    public string Payload { get; init; } = string.Empty;

    // Normalized side: empty unless the row was normalized.
    public int? NormalizedId { get; init; }

    public string? EmployeeNumber { get; init; }

    public string? EmployeeName { get; init; }

    public string? DeviceName { get; init; }

    public DateTime? LocalTime { get; init; }

    public string? TimeZoneId { get; init; }

    public string? EventType { get; init; }

    /// <summary>Gets how sure the type is: Certain, Inferred or Ambiguous.</summary>
    public string? Confidence { get; init; }

    /// <summary>Gets Unique, DuplicateConfirmed or DuplicateCandidate.</summary>
    public string? DuplicateStatus { get; init; }

    /// <summary>Gets None, FlaggedInvalidSequence, FlaggedUnrecognizedEmployee or FlaggedUnrecognizedDevice.</summary>
    public string? ExceptionStatus { get; init; }
}
