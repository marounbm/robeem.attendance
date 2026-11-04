using robeem.Application.Tenancy;

namespace robeem.Tests;

/// <summary>Tells whether the scope's tenant is the prime tenant, for a test: the tenant is <see cref="TestDatabase.Prime"/> or it is not.</summary>
public sealed class FixedPrimeTenantContext : IPrimeTenantContext
{
    private readonly string? _tenantId;

    public FixedPrimeTenantContext(string? tenantId)
    {
        _tenantId = tenantId;
    }

    public string PrimeTenantId => TestDatabase.Prime;

    public bool IsCurrentTenantPrime => string.Equals(_tenantId, TestDatabase.Prime, StringComparison.Ordinal);
}
