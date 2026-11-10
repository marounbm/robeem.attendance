using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using robeem.Contracts.Common;
using robeem.Contracts.HR.Leaves;

namespace robeem.Web.ApiClients;

public sealed class LeavesApiClient : ILeavesApiClient
{
    private const string Path = "api/leaves";

    private readonly HttpClient _http;

    public LeavesApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<PagedResult<LeaveListItem>> ListAsync(int? employeeId, string? search, DateTime? from, DateTime? to, string? type, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(CultureInfo.InvariantCulture),
            ["pageSize"] = pageSize.ToString(CultureInfo.InvariantCulture),
        };
        if (employeeId is { } id)
        {
            query["employeeId"] = id.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query["search"] = search;
        }

        if (from is { } first)
        {
            query["from"] = first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (to is { } last)
        {
            query["to"] = last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            query["type"] = type;
        }

        using var response = await _http.GetAsync(QueryHelpers.AddQueryString(Path, query), cancellationToken);
        return await ApiResponses.ReadAsync<PagedResult<LeaveListItem>>(response, cancellationToken);
    }

    public async Task<LeaveListItem?> GetAsync(int id, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(ItemPath(id), cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ApiResponses.ReadAsync<LeaveListItem>(response, cancellationToken);
    }

    public async Task<ApiSaveResult> CreateAsync(LeaveInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(Path, input, cancellationToken);
        return await ApiResponses.SaveAsync<LeaveInput>(response, null, cancellationToken);
    }

    public async Task<ApiSaveResult> UpdateAsync(int id, LeaveInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync(ItemPath(id), input, cancellationToken);
        return await ApiResponses.SaveAsync<LeaveInput>(response, id, cancellationToken);
    }

    public async Task<ApiSaveResult> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        using var response = await _http.DeleteAsync(ItemPath(id), cancellationToken);
        return await ApiResponses.SaveAsync<LeaveInput>(response, id, cancellationToken);
    }

    private static string ItemPath(int id) => $"{Path}/{id.ToString(CultureInfo.InvariantCulture)}";
}
