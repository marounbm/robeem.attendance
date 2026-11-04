using robeem.Application.Attendance;
using robeem.Application.Attendance.Catalog;
using robeem.Application.Security;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests;

/// <summary>
/// Builds the catalog rows a test needs, so a change to how a vendor, connector or integration is shaped is made in one place.
/// The catalog is the prime tenant's database (<see cref="TestDatabase.CreateCatalog"/>); an integration refers to its connector by key.
/// </summary>
internal static class AttendanceTestData
{
    /// <summary>A vendor and its one connector, both keyed <paramref name="key"/>; <see cref="Store"/> it in the catalog to store both.</summary>
    public static AttendanceConnectorDefinition Connector(string key, string? displayName = null, string? adapterKey = null, bool isActive = true) => new()
    {
        Vendor = new AttendanceVendor { VendorKey = key, DisplayName = displayName ?? key, IsActive = isActive },
        ConnectorKey = key,
        DisplayName = displayName ?? key,
        AdapterKey = adapterKey,
        IsActive = isActive,
    };

    /// <summary>Stores connectors (with their vendors, transports and payload formats) in the catalog of the test database, as the prime tenant.</summary>
    public static void Store(this TestDatabase database, params AttendanceConnectorDefinition[] connectors)
    {
        using var catalog = database.CreateCatalog();
        catalog.AttendanceConnectorDefinitions.AddRange(connectors);
        catalog.SaveChanges();
    }

    /// <summary>Gives an integration its default connection (an integration saved by the service always has one).</summary>
    public static AttendanceIntegration WithConnection(
        this AttendanceIntegration integration,
        AttendanceTransport transport = AttendanceTransport.FileUpload,
        string secretRef = "",
        string? schedule = null,
        ConnectionAuthMethod? method = null,
        string settings = "{}")
    {
        integration.Connections.Add(new AttendanceConnection
        {
            Name = "Default",
            IsDefault = true,
            Transport = transport,
            AuthMethod = method ?? transport switch
            {
                AttendanceTransport.RestApi => ConnectionAuthMethod.SessionLogin,
                AttendanceTransport.Webhook => ConnectionAuthMethod.HmacSignature,
                _ => ConnectionAuthMethod.None,
            },
            SecretRef = secretRef,
            PollingSchedule = schedule,
            Settings = settings,
        });
        return integration;
    }

    /// <summary>An integration made with the connector of this key (the catalog's seeded connectors share their vendor's key).</summary>
    public static AttendanceIntegration Integration(string connectorKey, string displayName) => new()
    {
        ConnectorKey = connectorKey,
        DisplayName = displayName,
    };

    /// <summary>An integration made with a connector.</summary>
    public static AttendanceIntegration Integration(AttendanceConnectorDefinition connector, string displayName) => Integration(connector.ConnectorKey, displayName);

    /// <summary>
    /// The catalog write access of the given tenant: it can write only as <see cref="TestDatabase.Prime"/>, and then as the given user (an
    /// administrator by default; work no user started when <paramref name="user"/> is the system user).
    /// </summary>
    public static ICatalogWriteAccess WriteAccess(string tenantId = TestDatabase.Prime, ICurrentUser? user = null) =>
        new CatalogWriteAccess(new FixedPrimeTenantContext(tenantId), user ?? new SystemCurrentUser());

    /// <summary>The payload format service, as the given tenant's scope (the prime tenant unless told) runs it: the catalog in its own database, the audit entry in the tenant's.</summary>
    public static AttendancePayloadFormatService PayloadFormatService(this TestDatabase database, RobeemDbContext tenantDb, string tenantId = TestDatabase.Prime, ICurrentUser? user = null, ICatalogReader? catalog = null) =>
        new(database.CreateCatalog(tenantId), catalog ?? database.CreateCatalogReader(), tenantDb, WriteAccess(tenantId, user), AuditOf(tenantDb, user), new CustomFormatFileReader(), new CustomFormatInterpreterFactory());

    public static AttendanceVendorService VendorService(this TestDatabase database, RobeemDbContext tenantDb, string tenantId = TestDatabase.Prime, ICurrentUser? user = null) =>
        new(database.CreateCatalog(tenantId), tenantDb, WriteAccess(tenantId, user), AuditOf(tenantDb, user));

    public static AttendanceDeviceTypeService DeviceTypeService(this TestDatabase database, RobeemDbContext tenantDb, string tenantId = TestDatabase.Prime, ICurrentUser? user = null, ICatalogReader? catalog = null) =>
        new(database.CreateCatalog(tenantId), catalog ?? database.CreateCatalogReader(), tenantDb, WriteAccess(tenantId, user), AuditOf(tenantDb, user));

    public static AttendanceTransportService TransportService(this TestDatabase database, RobeemDbContext tenantDb, string tenantId = TestDatabase.Prime, ICurrentUser? user = null) =>
        new(database.CreateCatalog(tenantId), tenantDb, WriteAccess(tenantId, user), AuditOf(tenantDb, user));

    public static AttendanceConnectorService ConnectorService(this TestDatabase database, RobeemDbContext tenantDb, IAttendanceAdapterCatalog? adapters = null, IPayloadDecryptorCatalog? decryptors = null, string tenantId = TestDatabase.Prime, ICurrentUser? user = null) =>
        new(database.CreateCatalog(tenantId), tenantDb, WriteAccess(tenantId, user), AuditOf(tenantDb, user), adapters ?? new StubAdapterCatalog(), decryptors);

    /// <summary>Adds a transport, by its definition, to a connector that has not been stored yet.</summary>
    public static AttendanceConnectorDefinition WithTransport(this AttendanceConnectorDefinition connector, AttendanceTransportDefinition definition)
    {
        connector.Transports.Add(new AttendanceConnectorTransport { Transport = definition });
        return connector;
    }

    private static AttendanceAuditTrail AuditOf(RobeemDbContext tenantDb, ICurrentUser? user) =>
        new(tenantDb, user ?? new TestUser(7), TimeProvider.System);
}
