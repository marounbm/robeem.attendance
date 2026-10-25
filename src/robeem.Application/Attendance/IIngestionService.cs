using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

public interface IIngestionService
{
    /// <summary>
    /// The integrations a file can be uploaded into, by name: active ones whose vendor is active and
    /// supports file upload. Each carries its vendor's file formats and layout, as the adapter describes them.
    /// </summary>
    Task<IReadOnlyList<UploadTarget>> ListUploadTargetsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stores every transaction of an uploaded file as a <c>RawAttendanceTransaction</c>, in a new
    /// ManualUpload batch, and returns the batch outcome; null when the integration does not exist.
    /// Row-level problems never throw: every row is stored (CLAUDE.md §1.1), and an unusable one is
    /// marked Failed and counted in <see cref="AttendanceIngestionResult.RecordsFailed"/>.
    /// </summary>
    Task<AttendanceIngestionResult?> IngestManualUploadAsync(int integrationId, string filePath, CancellationToken cancellationToken);

    /// <summary>
    /// <see cref="IngestManualUploadAsync"/> for a file a person uploaded: also records the file (its
    /// name, size, SHA-256, upload time and uploader) as an <c>AttendanceUploadedFile</c> pointing at
    /// the batch. The result carries the hash, and when the same bytes were uploaded before. The file
    /// is still ingested then: raw storage tolerates duplicates (CLAUDE.md §1.4).
    /// </summary>
    Task<AttendanceIngestionResult?> IngestUploadedFileAsync(int integrationId, string filePath, string fileName, int? uploadedById, CancellationToken cancellationToken);

    /// <summary>The upload history, newest first; only the integration's when <paramref name="integrationId"/> is given.</summary>
    Task<IReadOnlyList<UploadedFileItem>> ListUploadedFilesAsync(int? integrationId, int take, CancellationToken cancellationToken);

    /// <summary>
    /// The same for a body a webhook delivered (already authenticated by the caller), in a new
    /// Webhook batch, recorded against <paramref name="connectionId"/> (null: the integration's default connection). A body
    /// the adapter cannot read marks the batch Failed and stores no row.
    /// </summary>
    Task<AttendanceIngestionResult?> IngestWebhookPayloadAsync(int integrationId, int? connectionId, string filePath, CancellationToken cancellationToken);
}
