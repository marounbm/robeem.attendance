namespace robeem.Application.Attendance.Catalog;

/// <summary>The attendance catalog belongs to the prime tenant: any other tenant can read it but not change it.</summary>
public sealed class CatalogReadOnlyException : Exception
{
    public CatalogReadOnlyException()
        : base("The attendance catalog is managed by the prime tenant and is read-only here.")
    {
    }
}
