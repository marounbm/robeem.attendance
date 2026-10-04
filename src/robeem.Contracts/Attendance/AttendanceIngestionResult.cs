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
}
