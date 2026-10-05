using System.Globalization;
using System.Net.Http.Json;
using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

public sealed class PayPeriodsApiClient : IPayPeriodsApiClient
{
    private const string Path = "api/attendance/pay-periods";

    private readonly HttpClient _http;

    public PayPeriodsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<PayPeriodItem>> ListAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(Path, cancellationToken);
        return await ApiResponses.ReadAsync<List<PayPeriodItem>>(response, cancellationToken);
    }

    public async Task<ApiSaveResult> CreateAsync(PayPeriodInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(Path, input, cancellationToken);
        return await ApiResponses.SaveAsync<PayPeriodInput>(response, null, cancellationToken);
    }

    public async Task<ApiSaveResult> CloseAsync(int id, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsync($"{Path}/{Number(id)}/close", null, cancellationToken);
        return await ApiResponses.SaveAsync<PayPeriodInput>(response, id, cancellationToken);
    }

    public async Task<ApiSaveResult> ReopenAsync(int id, string reason, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync($"{Path}/{Number(id)}/reopen", new ReopenPayPeriodRequest { Reason = reason }, cancellationToken);
        return await ApiResponses.SaveAsync<ReopenPayPeriodRequest>(response, id, cancellationToken);
    }

    private static string Number(int id) => id.ToString(CultureInfo.InvariantCulture);
}
