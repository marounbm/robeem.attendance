namespace robeem.Application.Tenancy;

public interface ITenantRegistry
{
    IReadOnlyList<TenantDefinition> Tenants { get; }

    TenantDefinition? DefaultTenant { get; }

    /// <summary>Finds a tenant by id (case-insensitive); <c>null</c> when unknown.</summary>
    TenantDefinition? Find(string? tenantId);
}
