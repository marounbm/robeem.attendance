namespace robeem.Application.Attendance;

/// <summary>
/// Marks an <see cref="IAttendanceProvider"/> whose <c>RetrieveAttendanceTransactionsAsync</c> reads
/// <see cref="AttendanceIntegrationConfig.UploadFilePath"/>. A provider that is not one (it pulls from
/// an API, say) cannot be handed an uploaded file: the ingestion service reads the file with the
/// generic CSV adapter instead, and the rows keep the integration's own provider key.
/// </summary>
public interface IAttendanceFileReader
{
}
