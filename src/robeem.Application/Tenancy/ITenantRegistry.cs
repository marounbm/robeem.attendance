namespace robeem.Application.Tenancy;

public interface ITenantRegistry
{
    IReadOnlyList<TenantDefinition> Tenants { get; }

    TenantDefinition? DefaultTenant { get; }

    /// <summary>
    /// Gets the prime tenant: the tenant that owns the platform data every other tenant reads (the attendance catalog).
    /// It is also an ordinary tenant, with its own tenant data in the same database.
    /// </summary>
    TenantDefinition PrimeTenant { get; }

    /// <summary>Finds a tenant by id (case-insensitive); <c>null</c> when unknown.</summary>
    TenantDefinition? Find(string? tenantId);
}
