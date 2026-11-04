using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using robeem.DataModel;

namespace robeem.Application.Persistence;

/// <summary>
/// The attendance catalog: vendors, connectors, transports, device types and payload formats. It lives in the prime tenant's
/// database and is the same for every tenant, so it is not tenant-scoped. Every tenant reads it; only the prime tenant can
/// save changes to it (implemented by <c>robeem.Infrastructure.Persistence.Catalog.CatalogDbContext</c>, which refuses the
/// save of any other tenant). Tenant data refers to catalog rows by key, never by foreign key.
/// </summary>
public interface ICatalogDbContext
{
    DbSet<AttendanceVendor> AttendanceVendors { get; }

    DbSet<AttendanceConnectorDefinition> AttendanceConnectorDefinitions { get; }

    DbSet<AttendanceConnectorTransport> AttendanceConnectorTransports { get; }

    DbSet<AttendanceTransportDefinition> AttendanceTransportDefinitions { get; }

    DbSet<AttendanceDeviceType> AttendanceDeviceTypes { get; }

    DbSet<AttendancePayloadFormat> AttendancePayloadFormats { get; }

    DbSet<AttendancePayloadEventCode> AttendancePayloadEventCodes { get; }

    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
