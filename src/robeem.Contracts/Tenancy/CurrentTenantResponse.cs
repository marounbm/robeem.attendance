namespace robeem.Contracts.Tenancy;

/// <summary>The tenant the API resolved for the request.</summary>
public sealed class CurrentTenantResponse
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the caller may pick another tenant (development only).</summary>
    public bool CanSelectTenant { get; init; }
}
