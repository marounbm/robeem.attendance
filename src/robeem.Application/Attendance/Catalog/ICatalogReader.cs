using robeem.DataModel;

namespace robeem.Application.Attendance.Catalog;

/// <summary>
/// How tenant code reads the attendance catalog of the prime tenant. Tenant data refers to the catalog by key (connector key,
/// payload format name, device type name), never by foreign key, so everything here is looked up by key. Reads only: changing the
/// catalog is the catalog services' business, in the prime tenant.
/// </summary>
public interface ICatalogReader
{
    /// <summary>Gets a connector by its key; null when unknown.</summary>
    Task<ConnectorInfo?> GetConnectorAsync(string connectorKey, CancellationToken cancellationToken);

    /// <summary>Gets the connectors with these keys, keyed by connector key. A key with no connector is missing from the result.</summary>
    Task<IReadOnlyDictionary<string, ConnectorInfo>> GetConnectorsAsync(IEnumerable<string> connectorKeys, CancellationToken cancellationToken);

    /// <summary>Lists every connector, ordered by vendor name then connector name.</summary>
    Task<IReadOnlyList<ConnectorInfo>> ListConnectorsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the latest version of the payload format with this name, with its event codes, for reading. Not tracked.
    /// Null when there is no such format. A deactivated format is still returned: what reads a file is not a choice.
    /// </summary>
    Task<AttendancePayloadFormat?> GetLatestFormatAsync(string formatName, CancellationToken cancellationToken);

    /// <summary>Gets one exact version of a payload format, with its event codes. Not tracked; null when unknown.</summary>
    Task<AttendancePayloadFormat?> GetFormatVersionAsync(string formatName, int version, CancellationToken cancellationToken);

    /// <summary>Gets a payload format of any version by its catalog id, with its event codes. Not tracked; null when unknown. For the screens that show a format the user picked from a list; tenant data holds a name, never an id.</summary>
    Task<AttendancePayloadFormat?> GetFormatByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Gets the latest version of each named payload format, keyed by name. A name with no format is missing from the result.</summary>
    Task<IReadOnlyDictionary<string, AttendancePayloadFormat>> GetLatestFormatsAsync(IEnumerable<string> formatNames, CancellationToken cancellationToken);

    /// <summary>Lists the latest version of every payload format, ordered by name. With event codes. Not tracked.</summary>
    Task<IReadOnlyList<AttendancePayloadFormat>> ListLatestFormatsAsync(CancellationToken cancellationToken);

    /// <summary>Lists the transport definitions, ordered by name. Not tracked.</summary>
    Task<IReadOnlyList<AttendanceTransportDefinition>> ListTransportsAsync(CancellationToken cancellationToken);

    /// <summary>Lists the device types, ordered by name. Not tracked.</summary>
    Task<IReadOnlyList<AttendanceDeviceType>> ListDeviceTypesAsync(CancellationToken cancellationToken);
}
