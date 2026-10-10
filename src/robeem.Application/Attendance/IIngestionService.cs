using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

public interface IIngestionService
{
    /// <summary>
    /// Stores every transaction of an uploaded file as a <c>RawAttendanceTransaction</c>, in a new
    /// ManualUpload batch, and returns the batch outcome; null when the integration does not exist.
    /// Row-level problems never throw: every row is stored (CLAUDE.md §1.1), and an unusable one is
    /// marked Failed and counted in <see cref="AttendanceIngestionResult.RecordsFailed"/>.
    /// </summary>
    Task<AttendanceIngestionResult?> IngestManualUploadAsync(int integrationId, string filePath, CancellationToken cancellationToken);

    /// <summary>
    /// The same for a body a webhook delivered (already authenticated by the caller), in a new
    /// Webhook batch. A body the adapter cannot read marks the batch Failed and stores no row.
    /// </summary>
    Task<AttendanceIngestionResult?> IngestWebhookPayloadAsync(int integrationId, string filePath, CancellationToken cancellationToken);
}
