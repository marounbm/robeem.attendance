using robeem.Application.Security;
using robeem.Application.Tenancy;

namespace robeem.Application.Attendance.Catalog;

/// <inheritdoc />
public sealed class CatalogWriteAccess : ICatalogWriteAccess
{
    /// <summary>The name of the role that manages the catalog. Mirrors <c>AttendanceRoles.PlatformAdministrator</c> in the Api.</summary>
    public const string PlatformRole = "PlatformAdministrator";

    private readonly IPrimeTenantContext _prime;
    private readonly ICurrentUser _user;

    public CatalogWriteAccess(IPrimeTenantContext prime, ICurrentUser user)
    {
        _prime = prime;
        _user = user;
    }

    public void EnsureCanWrite()
    {
        // The Api has already required the PlatformAdministrator role; this is the second line behind it. Work no user started
        // (seeding, a job) is the system actor and may write in the prime tenant.
        if (!_prime.IsCurrentTenantPrime || (_user.IsAuthenticated && !_user.IsInRole(PlatformRole)))
        {
            throw new CatalogReadOnlyException();
        }
    }
}
