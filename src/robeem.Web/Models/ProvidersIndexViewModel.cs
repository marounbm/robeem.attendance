using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class ProvidersIndexViewModel
{
    public IReadOnlyList<ProviderListItem> Providers { get; init; } = Array.Empty<ProviderListItem>();
}
