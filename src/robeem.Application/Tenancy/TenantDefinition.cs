namespace robeem.Application.Tenancy;

/// <summary>One tenant and the connection string of its dedicated database.</summary>
public sealed class TenantDefinition
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string ConnectionString { get; set; } = string.Empty;
}
