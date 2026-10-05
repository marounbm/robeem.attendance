using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class PoliciesIndexViewModel
{
    public IReadOnlyList<PolicyListItem> Policies { get; init; } = Array.Empty<PolicyListItem>();
}
