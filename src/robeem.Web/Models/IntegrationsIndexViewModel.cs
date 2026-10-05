using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class IntegrationsIndexViewModel
{
    public IReadOnlyList<IntegrationListItem> Integrations { get; init; } = Array.Empty<IntegrationListItem>();
}
