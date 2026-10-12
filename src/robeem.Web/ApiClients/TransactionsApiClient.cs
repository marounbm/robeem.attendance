using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;
using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

public sealed class TransactionsApiClient : ITransactionsApiClient
{
    private const string Path = "api/attendance/transactions";

    private readonly HttpClient _http;

    public TransactionsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<RawTransactionListItem>> ListRawAsync(int? integrationId, int? batchId, string? status, DateTime? from, DateTime? to, string? search, int take, CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?> { ["take"] = Number(take) };
        Add(query, "integrationId", integrationId);
        Add(query, "batchId", batchId);
        Add(query, "status", status);
        Add(query, "from", from);
        Add(query, "to", to);
        Add(query, "search", search);

        using var response = await _http.GetAsync(QueryHelpers.AddQueryString($"{Path}/raw", query), cancellationToken);
        return await ApiResponses.ReadAsync<List<RawTransactionListItem>>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<NormalizedTransactionListItem>> ListNormalizedAsync(int? integrationId, int? rawId, string? employee, string? eventType, string? duplicate, string? flag, DateTime? from, DateTime? to, int take, CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?> { ["take"] = Number(take) };
        Add(query, "integrationId", integrationId);
        Add(query, "rawId", rawId);
        Add(query, "employee", employee);
        Add(query, "eventType", eventType);
        Add(query, "duplicate", duplicate);
        Add(query, "flag", flag);
        Add(query, "from", from);
        Add(query, "to", to);

        using var response = await _http.GetAsync(QueryHelpers.AddQueryString($"{Path}/normalized", query), cancellationToken);
        return await ApiResponses.ReadAsync<List<NormalizedTransactionListItem>>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<TransactionIntegrationOption>> ListIntegrationsAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{Path}/integrations", cancellationToken);
        return await ApiResponses.ReadAsync<List<TransactionIntegrationOption>>(response, cancellationToken);
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static void Add(Dictionary<string, string?> query, string name, int? value)
    {
        if (value is { } number)
        {
            query[name] = Number(number);
        }
    }

    private static void Add(Dictionary<string, string?> query, string name, DateTime? value)
    {
        if (value is { } date)
        {
            query[name] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }

    private static void Add(Dictionary<string, string?> query, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query[name] = value.Trim();
        }
    }
}
