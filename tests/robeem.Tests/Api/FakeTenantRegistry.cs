using robeem.Application.Tenancy;

namespace robeem.Tests.Api;

/// <summary>A tenant registry of the given ids, for the scheduler tests.</summary>
internal sealed class FakeTenantRegistry : ITenantRegistry
{
    public FakeTenantRegistry(params string[] ids)
    {
        Tenants = ids.Select(id => new TenantDefinition { Id = id, Name = id, ConnectionString = "x" }).ToList();
    }

    public IReadOnlyList<TenantDefinition> Tenants { get; }

    public TenantDefinition? DefaultTenant => Tenants.FirstOrDefault();

    public TenantDefinition? Find(string? tenantId) => Tenants.FirstOrDefault(t => t.Id == tenantId);
}
