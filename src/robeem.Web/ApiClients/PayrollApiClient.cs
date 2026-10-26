using System.Globalization;
using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

public sealed class PayrollApiClient : IPayrollApiClient
{
    private readonly HttpClient _http;

    public PayrollApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<PayrollAttendanceDay>> GetApprovedAsync(DateTime? from, DateTime? to, string? employee, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"api/attendance/payroll/approved?{Query(from, to, employee)}", cancellationToken);
        return await ApiResponses.ReadAsync<List<PayrollAttendanceDay>>(response, cancellationToken);
    }

    public async Task<PayrollApprovedPage> GetApprovedPageAsync(DateTime? from, DateTime? to, string? employee, int page, int pageSize, CancellationToken cancellationToken)
    {
        var paging = $"page={page.ToString(CultureInfo.InvariantCulture)}&pageSize={pageSize.ToString(CultureInfo.InvariantCulture)}";
        using var response = await _http.GetAsync($"api/attendance/payroll/approved/page?{Query(from, to, employee, paging)}", cancellationToken);
        return await ApiResponses.ReadAsync<PayrollApprovedPage>(response, cancellationToken);
    }

    /// <summary>Builds the query string: the dates (when given), then any paging, then the employee (when given).</summary>
    private static string Query(DateTime? from, DateTime? to, string? employee, string? paging = null)
    {
        var parts = new List<string>();
        if (from is { } start)
        {
            parts.Add($"from={start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        }

        if (to is { } end)
        {
            parts.Add($"to={end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        }

        if (paging is not null)
        {
            parts.Add(paging);
        }

        if (!string.IsNullOrWhiteSpace(employee))
        {
            parts.Add($"employee={Uri.EscapeDataString(employee.Trim())}");
        }

        return string.Join('&', parts);
    }
}
