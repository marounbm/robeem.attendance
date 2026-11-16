using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

public sealed class DataResetApiClient : IDataResetApiClient
{
    private const string Path = "api/attendance/data-reset";

    private readonly HttpClient _http;

    public DataResetApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<AttendanceDataSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(Path, cancellationToken);
        return await ApiResponses.ReadAsync<AttendanceDataSummary>(response, cancellationToken);
    }

    public async Task<ApiCommandResult<IReadOnlyList<AttendanceDataCount>>> ResetAsync(string confirmation, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(Path, new AttendanceDataResetRequest { Confirmation = confirmation }, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            // The Api says why in the problem's detail: not turned on, or the tenant id was not typed.
            return ApiCommandResult<IReadOnlyList<AttendanceDataCount>>.Refused([await ReadDetailAsync(response, cancellationToken) ?? "The data can't be deleted now."]);
        }

        await ApiResponses.EnsureSuccessAsync(response, cancellationToken);
        var deleted = await response.Content.ReadFromJsonAsync<List<AttendanceDataCount>>(cancellationToken) ?? [];
        return ApiCommandResult<IReadOnlyList<AttendanceDataCount>>.Ok(deleted);
    }

    private static async Task<string?> ReadDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
