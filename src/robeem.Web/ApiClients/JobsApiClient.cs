using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

public sealed class JobsApiClient : IJobsApiClient
{
    private const string Path = "api/attendance/jobs";

    private readonly HttpClient _http;

    public JobsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<JobStatusItem>> ListAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(Path, cancellationToken);
        return await ApiResponses.ReadAsync<List<JobStatusItem>>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<JobRunItem>> ListRunsAsync(string? jobName, string? status, int take, CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?> { ["take"] = take.ToString(CultureInfo.InvariantCulture) };
        if (!string.IsNullOrWhiteSpace(jobName))
        {
            query["jobName"] = jobName;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query["status"] = status;
        }

        using var response = await _http.GetAsync(QueryHelpers.AddQueryString($"{Path}/runs", query), cancellationToken);
        return await ApiResponses.ReadAsync<List<JobRunItem>>(response, cancellationToken);
    }

    public async Task<ApiCommandResult<RunJobResponse>> RunAsync(string jobName, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsync($"{Path}/{Uri.EscapeDataString(jobName)}/run", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            // The Api says why in the problem's detail: running already, or switched off.
            var detail = await ReadDetailAsync(response, cancellationToken);
            return ApiCommandResult<RunJobResponse>.Refused([detail ?? "The job can't be started now."]);
        }

        await ApiResponses.EnsureSuccessAsync(response, cancellationToken);
        var started = await response.Content.ReadFromJsonAsync<RunJobResponse>(cancellationToken) ?? new RunJobResponse();
        return ApiCommandResult<RunJobResponse>.Ok(started);
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
