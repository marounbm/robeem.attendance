using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's file upload (<c>api/attendance/ingestion</c>): where a file can go, and sending it.</summary>
public interface IUploadsApiClient
{
    /// <summary>Lists the integrations a file can be uploaded into, each with its vendor's file formats and layout.</summary>
    Task<IReadOnlyList<UploadTarget>> ListTargetsAsync(CancellationToken cancellationToken);

    /// <summary>Lists one page of the files uploaded so far, newest first, each with its SHA-256 and upload time. A page past the end answers the last.</summary>
    Task<PagedResult<UploadedFileItem>> ListUploadsAsync(int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Uploads an attendance file into the integration's raw transactions.</summary>
    Task<ApiCommandResult<AttendanceIngestionResult>> UploadAsync(int integrationId, Stream file, string fileName, CancellationToken cancellationToken);
}
