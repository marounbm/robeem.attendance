using robeem.Contracts.Tenancy;

namespace robeem.Web.Models;

public sealed class TenantPickerViewModel
{
    public IReadOnlyList<TenantSummary> Tenants { get; init; } = Array.Empty<TenantSummary>();

    public string? CurrentTenantId { get; init; }

    public string? ReturnUrl { get; init; }
}
