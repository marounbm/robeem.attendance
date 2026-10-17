using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class TransportsIndexViewModel
{
    public IReadOnlyList<TransportItem> Transports { get; init; } = Array.Empty<TransportItem>();
}
