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

    public async Task<IReadOnlyList<PayrollAttendanceDay>> GetApprovedAsync(DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var query = $"from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        using var response = await _http.GetAsync($"api/attendance/payroll/approved?{query}", cancellationToken);
        return await ApiResponses.ReadAsync<List<PayrollAttendanceDay>>(response, cancellationToken);
    }

    public async Task<PayrollApprovedPage> GetApprovedPageAsync(DateTime from, DateTime to, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = $"from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&page={page.ToString(CultureInfo.InvariantCulture)}&pageSize={pageSize.ToString(CultureInfo.InvariantCulture)}";
        using var response = await _http.GetAsync($"api/attendance/payroll/approved/page?{query}", cancellationToken);
        return await ApiResponses.ReadAsync<PayrollApprovedPage>(response, cancellationToken);
    }
}
