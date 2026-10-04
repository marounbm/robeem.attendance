namespace robeem.Application.Tenancy;

public sealed class UnknownTenantException : InvalidOperationException
{
    public UnknownTenantException(string? tenantId)
        : base($"Tenant '{tenantId}' is not configured.")
    {
        TenantId = tenantId;
    }

    public string? TenantId { get; }
}
