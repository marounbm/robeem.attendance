using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Web.ApiClients;

public sealed class TransactionsApiClient : ITransactionsApiClient
{
    private const string Path = "api/attendance/transactions";

    private readonly HttpClient _http;

    public TransactionsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<PagedResult<RawTransactionListItem>> ListRawAsync(int? integrationId, int? batchId, string? status, DateTime? from, DateTime? to, string? search, string? employee, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?> { ["page"] = Number(page), ["pageSize"] = Number(pageSize) };
        Add(query, "integrationId", integrationId);
        Add(query, "batchId", batchId);
        Add(query, "status", status);
        Add(query, "from", from);
        Add(query, "to", to);
        Add(query, "search", search);
        Add(query, "employee", employee);

        using var response = await _http.GetAsync(QueryHelpers.AddQueryString($"{Path}/raw", query), cancellationToken);
        return await ApiResponses.ReadAsync<PagedResult<RawTransactionListItem>>(response, cancellationToken);
    }

    public async Task<PagedResult<NormalizedTransactionListItem>> ListNormalizedAsync(int? integrationId, int? rawId, string? employee, string? eventType, string? duplicate, string? flag, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?> { ["page"] = Number(page), ["pageSize"] = Number(pageSize) };
        Add(query, "integrationId", integrationId);
        Add(query, "rawId", rawId);
        Add(query, "employee", employee);
        Add(query, "eventType", eventType);
        Add(query, "duplicate", duplicate);
        Add(query, "flag", flag);
        Add(query, "from", from);
        Add(query, "to", to);

        using var response = await _http.GetAsync(QueryHelpers.AddQueryString($"{Path}/normalized", query), cancellationToken);
        return await ApiResponses.ReadAsync<PagedResult<NormalizedTransactionListItem>>(response, cancellationToken);
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
