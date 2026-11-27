using System.Globalization;
using System.Net.Http.Json;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Web.ApiClients;

public sealed class PayCyclesApiClient : IPayCyclesApiClient
{
    private const string Path = "api/attendance/pay-cycles";

    private readonly HttpClient _http;

    public PayCyclesApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<PayCycleItem> GetDefaultAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{Path}/default", cancellationToken);
        return await ApiResponses.ReadAsync<PayCycleItem>(response, cancellationToken);
    }

    public async Task<ApiSaveResult> SaveDefaultAsync(PayCycleInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync($"{Path}/default", input, cancellationToken);
        return await ApiResponses.SaveAsync<PayCycleInput>(response, null, cancellationToken);
    }

    public async Task<PagedResult<EmployeePayCycleItem>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = $"page={Number(page)}&pageSize={Number(pageSize)}" + (string.IsNullOrWhiteSpace(search) ? string.Empty : $"&search={Uri.EscapeDataString(search)}");
        using var response = await _http.GetAsync($"{Path}?{query}", cancellationToken);
        return await ApiResponses.ReadAsync<PagedResult<EmployeePayCycleItem>>(response, cancellationToken);
    }

    public async Task<PayCycleInput?> GetEmployeeAsync(int employeeId, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{Path}/employees/{Number(employeeId)}", cancellationToken);
        return response.StatusCode == System.Net.HttpStatusCode.NotFound ? null : await ApiResponses.ReadAsync<PayCycleInput>(response, cancellationToken);
    }

    public async Task<ApiSaveResult> SaveEmployeeAsync(int employeeId, PayCycleInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync($"{Path}/employees/{Number(employeeId)}", input, cancellationToken);
        return await ApiResponses.SaveAsync<PayCycleInput>(response, employeeId, cancellationToken);
    }

    public async Task<ApiSaveResult> RemoveEmployeeAsync(int employeeId, CancellationToken cancellationToken)
    {
        using var response = await _http.DeleteAsync($"{Path}/employees/{Number(employeeId)}", cancellationToken);
        return await ApiResponses.SaveAsync<PayCycleInput>(response, employeeId, cancellationToken);
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
