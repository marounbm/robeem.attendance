namespace robeem.Infrastructure.Tenancy;

/// <summary>Tenant context with a fixed tenant, for design-time tooling and tests.</summary>
public sealed class FixedTenantContext : ITenantContext
{
    public FixedTenantContext(string? tenantId)
    {
        TenantId = tenantId;
    }

    public string? TenantId { get; }
}
