using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests;

/// <summary>
/// Builds the catalog rows a test needs, so a change to how a vendor, connector or integration is shaped is made in one place.
/// </summary>
internal static class AttendanceTestData
{
    /// <summary>A vendor and its one connector, both keyed <paramref name="key"/>; add it to a context to store both.</summary>
    public static AttendanceConnectorDefinition Connector(string key, string? displayName = null, string? adapterKey = null, bool isActive = true) => new()
    {
        Vendor = new AttendanceVendor { VendorKey = key, DisplayName = displayName ?? key, IsActive = isActive },
        ConnectorKey = key,
        DisplayName = displayName ?? key,
        AdapterKey = adapterKey,
        IsActive = isActive,
    };

    /// <summary>The seeded connector with this key (the seeded vendors each have one of the same key).</summary>
    public static AttendanceConnectorDefinition Seeded(RobeemDbContext db, string key) =>
        db.AttendanceConnectorDefinitions.Single(c => c.ConnectorKey == key);

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

    /// <summary>An integration made with a connector.</summary>
    public static AttendanceIntegration Integration(AttendanceConnectorDefinition connector, string displayName) => new()
    {
        Connector = connector,
        DisplayName = displayName,
    };

    /// <summary>Adds a transport, by its definition's key, to a connector that has not been stored yet.</summary>
    public static AttendanceConnectorDefinition WithTransport(this AttendanceConnectorDefinition connector, AttendanceTransportDefinition definition)
    {
        connector.Transports.Add(new AttendanceConnectorTransport { Transport = definition });
        return connector;
    }
}
