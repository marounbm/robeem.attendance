namespace robeem.Application.HR.Seeding;

/// <summary>Which tenants receive the sample employees. Section <see cref="SectionName"/>; empty by default, so no tenant does.</summary>
public sealed class SampleDataOptions
{
    public const string SectionName = "Database:SampleData";

    /// <summary>Gets or sets the ids of the tenants that get the sample employees, e.g. <c>demo</c>. Never a production tenant.</summary>
    public string[] TenantIds { get; set; } = [];
}
