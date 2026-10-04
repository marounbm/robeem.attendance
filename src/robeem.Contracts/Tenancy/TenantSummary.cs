namespace robeem.Contracts.Tenancy;

/// <summary>A tenant as the front end may see it. Never carries the connection string.</summary>
public sealed class TenantSummary
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
}
