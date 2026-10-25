using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class ConnectionsIndexViewModel
{
    public IntegrationListItem Integration { get; init; } = new();

    public IReadOnlyList<ConnectionListItem> Connections { get; init; } = Array.Empty<ConnectionListItem>();
}
