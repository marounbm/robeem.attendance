using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

public sealed class RecordsApiClient : IRecordsApiClient
{
    private const string Path = "api/attendance/records";

    private readonly HttpClient _http;

    public RecordsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<RecordDayListItem>> ListAsync(DateTime? from, DateTime? to, int? employeeId, string? status, bool onlyWithExceptions, int take, CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?> { ["take"] = take.ToString(CultureInfo.InvariantCulture) };
        if (from is { } first)
        {
            query["from"] = first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (to is { } last)
        {
            query["to"] = last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (employeeId is { } employee)
        {
            query["employeeId"] = employee.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query["status"] = status;
        }

        if (onlyWithExceptions)
        {
            query["onlyWithExceptions"] = "true";
        }

        using var response = await _http.GetAsync(QueryHelpers.AddQueryString(Path, query), cancellationToken);
        return await ApiResponses.ReadAsync<List<RecordDayListItem>>(response, cancellationToken);
    }

    public async Task<RecordDayDetail?> GetDayAsync(int employeeId, DateTime date, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(DayPath(employeeId, date), cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ApiResponses.ReadAsync<RecordDayDetail>(response, cancellationToken);
    }

    public async Task<ReprocessResponse?> ReprocessAsync(int employeeId, DateTime date, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsync($"{DayPath(employeeId, date)}/reprocess", null, cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ApiResponses.ReadAsync<ReprocessResponse>(response, cancellationToken);
    }

    private static string DayPath(int employeeId, DateTime date) =>
        $"{Path}/{employeeId.ToString(CultureInfo.InvariantCulture)}/{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
}
