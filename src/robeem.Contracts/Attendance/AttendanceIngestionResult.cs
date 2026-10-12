namespace robeem.Contracts.Attendance;

/// <summary>Outcome of an ingestion batch.</summary>
public sealed class AttendanceIngestionResult
{
    public int BatchId { get; init; }

    /// <summary>Gets the batch status: Running, Succeeded, Failed or PartiallyFailed.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets the number of rows stored as raw transactions, failed ones included.</summary>
    public int RecordsIngested { get; init; }

    /// <summary>Gets the number of stored rows that are unusable, e.g. because of an unparseable timestamp.</summary>
    public int RecordsFailed { get; init; }

    public string? ErrorSummary { get; init; }

    /// <summary>Gets the SHA-256 of the uploaded file's bytes; null for a webhook body, which is not tracked as a file.</summary>
    public string? Sha256 { get; init; }

    /// <summary>Gets when the same file (same bytes) was uploaded before, or null when it is new. The rows are stored again; normalization marks them duplicates.</summary>
    public DateTimeOffset? SameFileUploadedAt { get; init; }
}
