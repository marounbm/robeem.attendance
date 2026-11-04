namespace robeem.Contracts.Tenancy;

/// <summary>The tenant the API resolved for the request.</summary>
public sealed class CurrentTenantResponse
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the caller may pick another tenant (development only).</summary>
    public bool CanSelectTenant { get; init; }

    /// <summary>
    /// Gets a value indicating whether this is the prime tenant: the one that owns and manages the platform catalog every tenant reads.
    /// Every other tenant sees the catalog read-only.
    /// </summary>
    public bool IsPrime { get; init; }
}
