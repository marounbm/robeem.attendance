using robeem.Contracts.Tenancy;

namespace robeem.Web.ApiClients;

/// <summary>
/// robeem.Api could not work out which tenant the request is for: none was sent and there is no
/// default, or the tenant sent is not configured. Handled by <see cref="Tenancy.TenantSelectionRequiredFilter"/>.
/// </summary>
public sealed class TenantSelectionRequiredException : Exception
{
    public TenantSelectionRequiredException(string code)
        : base($"robeem.Api rejected the request's tenant ({code}).")
    {
        Code = code;
    }

    /// <summary>Gets the <see cref="TenancyProtocol"/> error code the API returned.</summary>
    public string Code { get; }

    public bool IsUnknownTenant => Code == TenancyProtocol.UnknownTenantCode;
}
