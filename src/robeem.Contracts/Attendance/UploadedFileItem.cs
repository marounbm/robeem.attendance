namespace robeem.Contracts.Attendance;

/// <summary>One line of the upload history: a file somebody uploaded, identified by its SHA-256.</summary>
public sealed class UploadedFileItem
{
    public int Id { get; init; }

    public int IntegrationId { get; init; }

    public string IntegrationName { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public long FileSizeBytes { get; init; }

    /// <summary>Gets the SHA-256 of the file's bytes, 64 lower-case hexadecimal characters.</summary>
    public string Sha256 { get; init; } = string.Empty;

    public DateTimeOffset UploadedAt { get; init; }

    /// <summary>Gets the uploader's name; empty when the upload had no employee profile behind it.</summary>
    public string UploadedByName { get; init; } = string.Empty;

    public int BatchId { get; init; }

    /// <summary>Gets the batch status: Succeeded, PartiallyFailed or Failed.</summary>
    public string Status { get; init; } = string.Empty;

    public int RecordsIngested { get; init; }

    public int RecordsFailed { get; init; }

    /// <summary>Gets a value indicating whether an earlier upload of this tenant had exactly the same bytes.</summary>
    public bool RepeatsAnEarlierUpload { get; init; }
}
