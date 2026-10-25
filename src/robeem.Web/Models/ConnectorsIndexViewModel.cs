using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class ConnectorsIndexViewModel
{
    public IReadOnlyList<ConnectorListItem> Connectors { get; init; } = Array.Empty<ConnectorListItem>();
}
