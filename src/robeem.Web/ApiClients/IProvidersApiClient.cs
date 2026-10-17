using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's provider catalog (<c>api/attendance/providers</c>).</summary>
public interface IProvidersApiClient
{
    Task<IReadOnlyList<ProviderListItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Gets a provider as editable input, or null when it does not exist.</summary>
    Task<ProviderInput?> GetInputAsync(int id, CancellationToken cancellationToken);

    Task<ApiSaveResult> CreateAsync(ProviderInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> UpdateAsync(int id, ProviderInput input, CancellationToken cancellationToken);
}
