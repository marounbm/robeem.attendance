using robeem.Application.Common;
using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>
/// The tenant's catalog of connectors: the software and protocols of each vendor, with the transports they support, the
/// payload format an upload is read by and the code adapter, if any. A connector is deactivated, never deleted:
/// integrations point at it. It is usable when it and its vendor are active and something can read what it delivers.
/// </summary>
public interface IAttendanceConnectorService
{
    Task<IReadOnlyList<ConnectorListItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Gets a connector as editable input, or null when it does not exist.</summary>
    Task<ConnectorInput?> GetInputAsync(int id, CancellationToken cancellationToken);

    Task<SaveResult> CreateAsync(ConnectorInput input, CancellationToken cancellationToken);

    /// <summary>Updates a connector. Its key and its vendor can't change: integrations and the raw punches they produced are tied to them.</summary>
    Task<SaveResult> UpdateAsync(int id, ConnectorInput input, CancellationToken cancellationToken);
}
