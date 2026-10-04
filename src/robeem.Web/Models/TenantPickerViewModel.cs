using robeem.Infrastructure.Tenancy;

namespace robeem.Web.Models;

public sealed class TenantPickerViewModel
{
    public IReadOnlyList<TenantDefinition> Tenants { get; init; } = Array.Empty<TenantDefinition>();

    public string? CurrentTenantId { get; init; }

    public string? ReturnUrl { get; init; }
}
