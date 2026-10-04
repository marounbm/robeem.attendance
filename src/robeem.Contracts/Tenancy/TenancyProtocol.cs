namespace robeem.Contracts.Tenancy;

/// <summary>How a caller tells robeem.Api which tenant it works for, and how the API reports tenant errors.</summary>
public static class TenancyProtocol
{
    /// <summary>Request header naming the tenant. Honoured only while unauthenticated tenant selection is allowed (development).</summary>
    public const string TenantHeader = "X-Tenant-Id";

    /// <summary>ProblemDetails extension that carries one of the error codes below.</summary>
    public const string ErrorCodeExtension = "code";

    /// <summary>403: the request needs a tenant and none could be resolved.</summary>
    public const string TenantRequiredCode = "tenant_required";

    /// <summary>400: the <see cref="TenantHeader"/> names a tenant that is not configured.</summary>
    public const string UnknownTenantCode = "unknown_tenant";

    /// <summary>403: the authenticated user's tenant is not configured.</summary>
    public const string TenantUnavailableCode = "tenant_unavailable";
}
