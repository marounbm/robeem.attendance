namespace robeem.Infrastructure.Tenancy;

/// <summary>A write would have created or changed data belonging to a tenant other than the current one.</summary>
public sealed class TenantIsolationException : InvalidOperationException
{
    public TenantIsolationException(string message)
        : base(message)
    {
    }
}
