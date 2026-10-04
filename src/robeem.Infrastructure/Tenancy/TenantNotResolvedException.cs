namespace robeem.Infrastructure.Tenancy;

/// <summary>Tenant data was accessed from a scope that has no tenant. Fails closed.</summary>
public sealed class TenantNotResolvedException : InvalidOperationException
{
    public TenantNotResolvedException()
        : base("No tenant has been resolved for the current scope; tenant data cannot be accessed.")
    {
    }
}
