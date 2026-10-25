using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's connector catalog (<c>api/attendance/connectors</c>).</summary>
public interface IConnectorsApiClient
{
    Task<IReadOnlyList<ConnectorListItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Gets a connector as editable input, or null when it does not exist.</summary>
    Task<ConnectorInput?> GetInputAsync(int id, CancellationToken cancellationToken);

    Task<ApiSaveResult> CreateAsync(ConnectorInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> UpdateAsync(int id, ConnectorInput input, CancellationToken cancellationToken);
}
