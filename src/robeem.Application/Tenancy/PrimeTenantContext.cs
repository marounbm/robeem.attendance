namespace robeem.Application.Tenancy;

/// <inheritdoc />
public sealed class PrimeTenantContext : IPrimeTenantContext
{
    private readonly ITenantContext _tenant;
    private readonly ITenantRegistry _registry;

    public PrimeTenantContext(ITenantContext tenant, ITenantRegistry registry)
    {
        _tenant = tenant;
        _registry = registry;
    }

    public string PrimeTenantId => _registry.PrimeTenant.Id;

    public bool IsCurrentTenantPrime
        => string.Equals(_tenant.TenantId, _registry.PrimeTenant.Id, StringComparison.OrdinalIgnoreCase);
}
