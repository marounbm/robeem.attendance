using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's connections of an integration (<c>api/attendance/integrations/{id}/connections</c>) and the transport descriptors.</summary>
public interface IConnectionsApiClient
{
    /// <summary>Lists the connections of an integration, the default first; null when the integration does not exist.</summary>
    Task<IReadOnlyList<ConnectionListItem>?> ListAsync(int integrationId, CancellationToken cancellationToken);

    /// <summary>Gets a connection as editable input, or null when it does not exist.</summary>
    Task<ConnectionInput?> GetInputAsync(int integrationId, int connectionId, CancellationToken cancellationToken);

    Task<ApiSaveResult> CreateAsync(int integrationId, ConnectionInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> UpdateAsync(int integrationId, int connectionId, ConnectionInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> MakeDefaultAsync(int integrationId, int connectionId, CancellationToken cancellationToken);

    /// <summary>Tests a connection; null when it does not exist.</summary>
    Task<ConnectionTestResult?> TestAsync(int integrationId, int connectionId, CancellationToken cancellationToken);

    /// <summary>What each transport declares: its settings and the authentication it allows.</summary>
    Task<IReadOnlyList<TransportDescriptorItem>> ListDescriptorsAsync(CancellationToken cancellationToken);
}
