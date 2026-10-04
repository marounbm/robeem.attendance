using System.Net;
using robeem.Contracts.Tenancy;

namespace robeem.Web.ApiClients;

public sealed class TenantsApiClient : ITenantsApiClient
{
    private const string TenantsPath = "api/tenants";

    private readonly HttpClient _http;

    public TenantsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<TenantSummary>?> ListAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(TenantsPath, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ApiResponses.ReadAsync<List<TenantSummary>>(response, cancellationToken);
    }

    public async Task<CurrentTenantResponse?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{TenantsPath}/current", cancellationToken);
        try
        {
            return await ApiResponses.ReadAsync<CurrentTenantResponse>(response, cancellationToken);
        }
        catch (TenantSelectionRequiredException)
        {
            return null;
        }
    }
}
