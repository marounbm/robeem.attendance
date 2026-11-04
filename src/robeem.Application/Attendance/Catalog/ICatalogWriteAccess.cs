namespace robeem.Application.Attendance.Catalog;

/// <summary>Who may change the platform catalog: a platform administrator, acting in the prime tenant.</summary>
public interface ICatalogWriteAccess
{
    /// <summary>Throws <see cref="CatalogReadOnlyException"/> unless the scope's tenant is the prime tenant.</summary>
    void EnsureCanWrite();
}
