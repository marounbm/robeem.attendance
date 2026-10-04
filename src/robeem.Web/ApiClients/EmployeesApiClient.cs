using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using robeem.Contracts.Common;
using robeem.Contracts.HR.Employees;

namespace robeem.Web.ApiClients;

public sealed class EmployeesApiClient : IEmployeesApiClient
{
    private const string EmployeesPath = "api/employees";
    private const string SchedulesPath = "api/schedules";

    private readonly HttpClient _http;

    public EmployeesApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<EmployeeListItem>> ListAsync(string? search, bool includeInactive, CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?> { ["includeInactive"] = includeInactive ? "true" : "false" };
        if (!string.IsNullOrWhiteSpace(search))
        {
            query["search"] = search;
        }

        using var response = await _http.GetAsync(QueryHelpers.AddQueryString(EmployeesPath, query), cancellationToken);
        return await ApiResponses.ReadAsync<List<EmployeeListItem>>(response, cancellationToken);
    }

    public async Task<EmployeeInput?> GetAsync(int id, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(EmployeePath(id), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ApiResponses.ReadAsync<EmployeeInput>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ScheduleOption>> ListSchedulesAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(SchedulesPath, cancellationToken);
        return await ApiResponses.ReadAsync<List<ScheduleOption>>(response, cancellationToken);
    }

    public async Task<EmployeeSaveResult> CreateAsync(EmployeeInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(EmployeesPath, input, cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            return await InvalidAsync(response, cancellationToken);
        }

        var created = await ApiResponses.ReadAsync<CreatedResponse>(response, cancellationToken);
        return EmployeeSaveResult.Success(created.Id);
    }

    public async Task<EmployeeSaveResult> UpdateAsync(int id, EmployeeInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync(EmployeePath(id), input, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return EmployeeSaveResult.Missing();
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            return await InvalidAsync(response, cancellationToken);
        }

        await ApiResponses.EnsureSuccessAsync(response, cancellationToken);
        return EmployeeSaveResult.Success(id);
    }

    private static string EmployeePath(int id) => $"{EmployeesPath}/{id.ToString(CultureInfo.InvariantCulture)}";

    private static async Task<EmployeeSaveResult> InvalidAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await ApiResponses.ThrowIfTenantRejectedAsync(response, cancellationToken);
        return EmployeeSaveResult.Invalid(await ApiResponses.ReadValidationErrorsAsync<EmployeeInput>(response, cancellationToken));
    }
}
