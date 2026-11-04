using robeem.Application.Tenancy;

namespace robeem.Infrastructure.Tenancy;

/// <summary>Bound from the <c>Tenancy</c> configuration section.</summary>
public sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";

    public List<TenantDefinition> Tenants { get; set; } = new();

    /// <summary>
    /// Gets or sets the id of the prime tenant, which owns the platform data every tenant reads (the attendance catalog) and alone may
    /// change it. Required; must be one of <see cref="Tenants"/>. The catalog lives in that tenant's database.
    /// </summary>
    public string? PrimeTenantId { get; set; }

    /// <summary>Tenant used when a request carries no tenant at all. Development convenience only.</summary>
    public string? DefaultTenantId { get; set; }

    /// <summary>
    /// When <c>true</c>, an unauthenticated caller may pick its tenant (header, cookie, default).
    /// Must stay <c>false</c> in production, where the tenant comes only from the authenticated
    /// user's <c>tenant_id</c> claim.
    /// </summary>
    public bool AllowUnauthenticatedTenantSelection { get; set; }
}
