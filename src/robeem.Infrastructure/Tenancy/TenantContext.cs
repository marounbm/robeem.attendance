using robeem.Application.Tenancy;

namespace robeem.Infrastructure.Tenancy;

/// <summary>
/// Scoped tenant holder. Set exactly once per scope (by the HTTP middleware or
/// <see cref="TenantScopeFactory"/>); the tenant's database connection is chosen from it, so it
/// can never be switched to another tenant within the same scope.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private readonly ITenantRegistry _registry;

    public TenantContext(ITenantRegistry registry)
    {
        _registry = registry;
    }

    public string? TenantId => Tenant?.Id;

    public TenantDefinition? Tenant { get; private set; }

    public void SetTenant(string tenantId)
    {
        var tenant = _registry.Find(tenantId) ?? throw new UnknownTenantException(tenantId);

        if (Tenant is not null && !ReferenceEquals(Tenant, tenant))
        {
            throw new InvalidOperationException(
                $"This scope already works for tenant '{Tenant.Id}' and cannot switch to '{tenant.Id}'.");
        }

        Tenant = tenant;
    }
}
