using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;
using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

public sealed class ExceptionsApiClient : IExceptionsApiClient
{
    private const string Path = "api/attendance/exceptions";

    private readonly HttpClient _http;

    public ExceptionsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<ExceptionListItem>> ListAsync(string? status, DateTime? from, DateTime? to, int? employeeId, int take, CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?> { ["take"] = take.ToString(CultureInfo.InvariantCulture) };
        if (!string.IsNullOrWhiteSpace(status))
        {
            query["status"] = status;
        }

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

        using var response = await _http.GetAsync(QueryHelpers.AddQueryString(Path, query), cancellationToken);
        return await ApiResponses.ReadAsync<List<ExceptionListItem>>(response, cancellationToken);
    }
}
