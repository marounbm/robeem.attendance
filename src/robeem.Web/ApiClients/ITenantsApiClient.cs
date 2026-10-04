using robeem.Contracts.Tenancy;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's tenant endpoints (<c>api/tenants</c>).</summary>
public interface ITenantsApiClient
{
    /// <summary>Gets the tenants a user may pick, or <c>null</c> when the API does not allow picking (production).</summary>
    Task<IReadOnlyList<TenantSummary>?> ListAsync(CancellationToken cancellationToken);

    /// <summary>Gets the tenant the API resolves for this request, or <c>null</c> when it resolves none.</summary>
    Task<CurrentTenantResponse?> GetCurrentAsync(CancellationToken cancellationToken);
}
