using System.Globalization;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Web.ApiClients;

public sealed class UploadsApiClient : IUploadsApiClient
{
    private const string Path = "api/attendance/ingestion";

    private readonly HttpClient _http;

    public UploadsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<UploadTarget>> ListTargetsAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{Path}/targets", cancellationToken);
        return await ApiResponses.ReadAsync<List<UploadTarget>>(response, cancellationToken);
    }

    public async Task<PagedResult<UploadedFileItem>> ListUploadsAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{Path}/uploads?page={page.ToString(CultureInfo.InvariantCulture)}&pageSize={pageSize.ToString(CultureInfo.InvariantCulture)}", cancellationToken);
        return await ApiResponses.ReadAsync<PagedResult<UploadedFileItem>>(response, cancellationToken);
    }

    public async Task<ApiCommandResult<AttendanceIngestionResult>> UploadAsync(int integrationId, Stream file, string fileName, CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent { { new StreamContent(file), "file", fileName } };
        using var response = await _http.PostAsync($"{Path}/{integrationId.ToString(CultureInfo.InvariantCulture)}/uploads", content, cancellationToken);
        return await ApiResponses.CommandAsync<AttendanceIngestionResult>(response, cancellationToken);
    }
}
