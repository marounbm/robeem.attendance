using System.Net.Http.Json;
using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

public sealed class AttendanceSettingsApiClient : IAttendanceSettingsApiClient
{
    private const string Path = "api/attendance/settings";

    private readonly HttpClient _http;

    public AttendanceSettingsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<AttendanceSettingsItem> GetAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(Path, cancellationToken);
        return await ApiResponses.ReadAsync<AttendanceSettingsItem>(response, cancellationToken);
    }

    public async Task<ApiSaveResult> SaveAsync(AttendanceSettingsInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync(Path, input, cancellationToken);
        return await ApiResponses.SaveAsync<AttendanceSettingsInput>(response, null, cancellationToken);
    }
}
