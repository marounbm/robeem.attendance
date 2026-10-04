namespace robeem.Infrastructure.Tenancy;

/// <summary>Bound from the <c>Tenancy</c> configuration section.</summary>
public sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";

    public List<TenantDefinition> Tenants { get; set; } = new();

    /// <summary>Tenant used when a request carries no tenant at all. Development convenience only.</summary>
    public string? DefaultTenantId { get; set; }

    /// <summary>
    /// When <c>true</c>, an unauthenticated caller may pick its tenant (header, cookie, default).
    /// Must stay <c>false</c> in production, where the tenant comes only from the authenticated
    /// user's <c>tenant_id</c> claim.
    /// </summary>
    public bool AllowUnauthenticatedTenantSelection { get; set; }
}
