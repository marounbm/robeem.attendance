namespace robeem.Application.Tenancy;

/// <summary>The tenant the current scope (HTTP request, background job) works for.</summary>
public interface ITenantContext
{
    /// <summary>Gets the current tenant id, or <c>null</c> when no tenant has been resolved.</summary>
    string? TenantId { get; }
}
