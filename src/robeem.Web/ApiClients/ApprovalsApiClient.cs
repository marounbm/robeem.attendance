using System.Globalization;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Web.ApiClients;

public sealed class ApprovalsApiClient : IApprovalsApiClient
{
    private const string Path = "api/attendance/approvals";

    private readonly HttpClient _http;

    public ApprovalsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<PagedResult<ApprovalListItem>> ListPendingAsync(int? employeeId, DateTime? from, DateTime? to, string? type, string? reason, int page, int pageSize, CancellationToken cancellationToken)
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

        if (!string.IsNullOrWhiteSpace(type))
        {
            query["type"] = type;
        }

        if (!string.IsNullOrWhiteSpace(reason))
        {
            query["reason"] = reason;
        }

        using var response = await _http.GetAsync(QueryHelpers.AddQueryString($"{Path}/pending", query), cancellationToken);
        return await ApiResponses.ReadAsync<PagedResult<ApprovalListItem>>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<EmployeeOption>> ListEmployeeOptionsAsync(CancellationToken cancellationToken)
    {
        // The same people, by the same scope, as the attendance days the approvals belong to.
        using var response = await _http.GetAsync("api/attendance/records/employee-options", cancellationToken);
        return await ApiResponses.ReadAsync<List<EmployeeOption>>(response, cancellationToken);
    }

    public async Task<ApiCommandResult<ApprovalDecisionResponse>> DecideAsync(int approvalId, DecideApprovalRequest request, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync($"{Path}/{approvalId.ToString(CultureInfo.InvariantCulture)}/decision", request, cancellationToken);
        return await ApiResponses.CommandAsync<ApprovalDecisionResponse>(response, cancellationToken);
    }
}
