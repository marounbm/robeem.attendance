using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using robeem.Contracts.HR.Schedules;

namespace robeem.Web.ApiClients;

public sealed class TimeSchedulesApiClient : ITimeSchedulesApiClient
{
    private const string Path = "api/time-schedules";

    private readonly HttpClient _http;

    public TimeSchedulesApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<TimeScheduleListItem>> ListAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(Path, cancellationToken);
        return await ApiResponses.ReadAsync<List<TimeScheduleListItem>>(response, cancellationToken);
    }

    public async Task<TimeScheduleDetail?> GetAsync(int id, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(SchedulePath(id), cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ApiResponses.ReadAsync<TimeScheduleDetail>(response, cancellationToken);
    }

    public async Task<ApiSaveResult> CreateAsync(TimeScheduleInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(Path, input, cancellationToken);
        return await ApiResponses.SaveAsync<TimeScheduleInput>(response, null, cancellationToken);
    }

    public async Task<ApiSaveResult> UpdateAsync(int id, TimeScheduleInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync(SchedulePath(id), input, cancellationToken);
        return await ApiResponses.SaveAsync<TimeScheduleInput>(response, id, cancellationToken);
    }

    public async Task<ApiSaveResult> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        using var response = await _http.DeleteAsync(SchedulePath(id), cancellationToken);
        return await ApiResponses.SaveAsync<TimeScheduleInput>(response, id, cancellationToken);
    }

    public async Task<ApiSaveResult> AddShiftAsync(int id, ScheduleShiftInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync($"{SchedulePath(id)}/shifts", input, cancellationToken);
        return await ApiResponses.SaveAsync<ScheduleShiftInput>(response, null, cancellationToken);
    }

    public async Task<ApiSaveResult> UpdateShiftAsync(int id, int shiftId, ScheduleShiftInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync(ShiftPath(id, shiftId), input, cancellationToken);
        return await ApiResponses.SaveAsync<ScheduleShiftInput>(response, shiftId, cancellationToken);
    }

    public async Task<ApiSaveResult> DeleteShiftAsync(int id, int shiftId, CancellationToken cancellationToken)
    {
        using var response = await _http.DeleteAsync(ShiftPath(id, shiftId), cancellationToken);
        return await ApiResponses.SaveAsync<ScheduleShiftInput>(response, shiftId, cancellationToken);
    }

    public async Task<ApiSaveResult> AddTeamAsync(int id, ScheduleTeamInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync($"{SchedulePath(id)}/teams", input, cancellationToken);
        return await ApiResponses.SaveAsync<ScheduleTeamInput>(response, null, cancellationToken);
    }

    public async Task<ApiSaveResult> RenameTeamAsync(int id, int teamId, ScheduleTeamInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync(TeamPath(id, teamId), input, cancellationToken);
        return await ApiResponses.SaveAsync<ScheduleTeamInput>(response, teamId, cancellationToken);
    }

    public async Task<ApiSaveResult> DeleteTeamAsync(int id, int teamId, CancellationToken cancellationToken)
    {
        using var response = await _http.DeleteAsync(TeamPath(id, teamId), cancellationToken);
        return await ApiResponses.SaveAsync<ScheduleTeamInput>(response, teamId, cancellationToken);
    }

    public async Task<ApiSaveResult> SaveRosterAsync(int id, int teamId, ScheduleRosterInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync($"{TeamPath(id, teamId)}/roster", input, cancellationToken);
        return await ApiResponses.SaveAsync<ScheduleRosterInput>(response, teamId, cancellationToken);
    }

    public async Task<ApiSaveResult> SetTeamMembersAsync(int id, int teamId, ScheduleMembersInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync($"{TeamPath(id, teamId)}/members", input, cancellationToken);
        return await ApiResponses.SaveAsync<ScheduleMembersInput>(response, teamId, cancellationToken);
    }

    private static string SchedulePath(int id) => $"{Path}/{id.ToString(CultureInfo.InvariantCulture)}";

    private static string ShiftPath(int id, int shiftId) => $"{SchedulePath(id)}/shifts/{shiftId.ToString(CultureInfo.InvariantCulture)}";

    private static string TeamPath(int id, int teamId) => $"{SchedulePath(id)}/teams/{teamId.ToString(CultureInfo.InvariantCulture)}";
}
