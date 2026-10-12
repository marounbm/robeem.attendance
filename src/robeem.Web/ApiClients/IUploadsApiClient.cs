using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's file upload (<c>api/attendance/ingestion</c>): where a file can go, and sending it.</summary>
public interface IUploadsApiClient
{
    /// <summary>Lists the integrations a file can be uploaded into, each with its provider's file formats and layout.</summary>
    Task<IReadOnlyList<UploadTarget>> ListTargetsAsync(CancellationToken cancellationToken);

    /// <summary>Lists the files uploaded so far, newest first, each with its SHA-256 and upload time.</summary>
    Task<IReadOnlyList<UploadedFileItem>> ListUploadsAsync(int take, CancellationToken cancellationToken);

    /// <summary>Uploads an attendance file into the integration's raw transactions.</summary>
    Task<ApiCommandResult<AttendanceIngestionResult>> UploadAsync(int integrationId, Stream file, string fileName, CancellationToken cancellationToken);
}
