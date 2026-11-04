using System.Globalization;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Web.ApiClients;

public sealed class ReviewsApiClient : IReviewsApiClient
{
    private const string Path = "api/attendance/reviews";

    private readonly HttpClient _http;

    public ReviewsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<PagedResult<ReviewListItem>> ListPendingAsync(int? employeeId, DateTime? from, DateTime? to, string? reason, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(CultureInfo.InvariantCulture),
            ["pageSize"] = pageSize.ToString(CultureInfo.InvariantCulture),
        };
        if (employeeId is { } employee)
        {
            query["employeeId"] = employee.ToString(CultureInfo.InvariantCulture);
        }

        if (from is { } first)
        {
            query["from"] = first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (to is { } last)
        {
            query["to"] = last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(reason))
        {
            query["reason"] = reason;
        }

        using var response = await _http.GetAsync(QueryHelpers.AddQueryString($"{Path}/pending", query), cancellationToken);
        return await ApiResponses.ReadAsync<PagedResult<ReviewListItem>>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<EmployeeOption>> ListEmployeeOptionsAsync(CancellationToken cancellationToken)
    {
        // The same people, by the same scope, as the attendance days the records belong to.
        using var response = await _http.GetAsync("api/attendance/records/employee-options", cancellationToken);
        return await ApiResponses.ReadAsync<List<EmployeeOption>>(response, cancellationToken);
    }

    public async Task<ApiCommandResult<ReviewDecisionResponse>> DecideAsync(int recordId, DecideReviewRequest request, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync($"{Path}/{recordId.ToString(CultureInfo.InvariantCulture)}/decision", request, cancellationToken);
        return await ApiResponses.CommandAsync<ReviewDecisionResponse>(response, cancellationToken);
    }
}
