using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

public sealed class RulesApiClient : IRulesApiClient
{
    private const string Path = "api/attendance/rules";

    private readonly HttpClient _http;

    public RulesApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<RuleScopeDto>> ListScopesAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{Path}/scopes", cancellationToken);
        return await ApiResponses.ReadAsync<List<RuleScopeDto>>(response, cancellationToken);
    }

    public Task<RuleSetDto?> GetCurrentAsync(int? scheduleId, CancellationToken cancellationToken) => GetSetAsync("current", scheduleId, cancellationToken);

    public Task<RuleSetDto?> GetDraftAsync(int? scheduleId, CancellationToken cancellationToken) => GetSetAsync("draft", scheduleId, cancellationToken);

    public async Task<RuleSetDto?> GetAsync(int id, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{Path}/{Number(id)}", cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ApiResponses.ReadAsync<RuleSetDto>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<RuleSetSummaryDto>> ListHistoryAsync(int? scheduleId, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(WithSchedule($"{Path}/history", scheduleId), cancellationToken);
        return await ApiResponses.ReadAsync<List<RuleSetSummaryDto>>(response, cancellationToken);
    }

    public async Task<ApiSaveResult> CreateDraftAsync(int? scheduleId, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsync(WithSchedule($"{Path}/drafts", scheduleId), null, cancellationToken);
        return await ApiResponses.SaveAsync<RuleScopeDto>(response, null, cancellationToken);
    }

    public async Task<ApiSaveResult> SaveDraftAsync(int draftId, RulesSaveRequest request, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync($"{Path}/drafts/{Number(draftId)}", request, cancellationToken);
        return await ApiResponses.SaveAsync<RulesSaveRequest>(response, draftId, cancellationToken, keepNestedPaths: true);
    }

    public async Task<ApiSaveResult> PublishAsync(int draftId, string? notes, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync($"{Path}/drafts/{Number(draftId)}/publish", new PublishRuleSetRequest { Notes = notes }, cancellationToken);
        return await ApiResponses.SaveAsync<RulesSaveRequest>(response, draftId, cancellationToken, keepNestedPaths: true);
    }

    public async Task<ApiSaveResult> DiscardDraftAsync(int draftId, CancellationToken cancellationToken)
    {
        using var response = await _http.DeleteAsync($"{Path}/drafts/{Number(draftId)}", cancellationToken);
        return await ApiResponses.SaveAsync<RulesSaveRequest>(response, draftId, cancellationToken);
    }

    private static string Number(int id) => id.ToString(CultureInfo.InvariantCulture);

    private static string WithSchedule(string path, int? scheduleId) =>
        scheduleId is { } id ? QueryHelpers.AddQueryString(path, new Dictionary<string, string?> { ["scheduleId"] = Number(id) }) : path;

    private async Task<RuleSetDto?> GetSetAsync(string what, int? scheduleId, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(WithSchedule($"{Path}/{what}", scheduleId), cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ApiResponses.ReadAsync<RuleSetDto>(response, cancellationToken);
    }
}
