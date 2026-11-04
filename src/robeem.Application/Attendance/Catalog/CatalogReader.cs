using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.DataModel;

namespace robeem.Application.Attendance.Catalog;

/// <inheritdoc />
public sealed class CatalogReader : ICatalogReader
{
    private readonly ICatalogDbContext _catalog;
    private readonly IAttendanceAdapterCatalog _adapters;
    private readonly IPayloadDecryptorCatalog _decryptors;

    public CatalogReader(ICatalogDbContext catalog, IAttendanceAdapterCatalog adapters, IPayloadDecryptorCatalog decryptors)
    {
        _catalog = catalog;
        _adapters = adapters;
        _decryptors = decryptors;
    }

    public async Task<ConnectorInfo?> GetConnectorAsync(string connectorKey, CancellationToken cancellationToken)
    {
        var connectors = await GetConnectorsAsync([connectorKey], cancellationToken);
        return connectors.GetValueOrDefault(connectorKey);
    }

    public async Task<IReadOnlyDictionary<string, ConnectorInfo>> GetConnectorsAsync(IEnumerable<string> connectorKeys, CancellationToken cancellationToken)
    {
        var keys = connectorKeys.Distinct(StringComparer.Ordinal).ToList();
        if (keys.Count == 0)
        {
            return new Dictionary<string, ConnectorInfo>(StringComparer.Ordinal);
        }

        var connectors = await Connectors().Where(c => keys.Contains(c.ConnectorKey)).ToListAsync(cancellationToken);
        return ToInfo(connectors);
    }

    public async Task<IReadOnlyList<ConnectorInfo>> ListConnectorsAsync(CancellationToken cancellationToken)
    {
        var connectors = await Connectors().ToListAsync(cancellationToken);
        var infos = ToInfo(connectors);
        return connectors
            .OrderBy(c => c.Vendor!.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(c => infos[c.ConnectorKey])
            .ToList();
    }

    public async Task<AttendancePayloadFormat?> GetLatestFormatAsync(string formatName, CancellationToken cancellationToken)
    {
        var formats = await GetLatestFormatsAsync([formatName], cancellationToken);
        return formats.GetValueOrDefault(formatName);
    }

    public Task<AttendancePayloadFormat?> GetFormatVersionAsync(string formatName, int version, CancellationToken cancellationToken) =>
        _catalog.AttendancePayloadFormats.AsNoTracking()
            .Include(f => f.EventCodes)
            .SingleOrDefaultAsync(f => f.Name == formatName && f.Version == version, cancellationToken);

    public Task<AttendancePayloadFormat?> GetFormatByIdAsync(int id, CancellationToken cancellationToken) =>
        _catalog.AttendancePayloadFormats.AsNoTracking()
            .Include(f => f.EventCodes)
            .SingleOrDefaultAsync(f => f.Id == id, cancellationToken);

    public async Task<IReadOnlyDictionary<string, AttendancePayloadFormat>> GetLatestFormatsAsync(IEnumerable<string> formatNames, CancellationToken cancellationToken)
    {
        var names = formatNames.Distinct(StringComparer.Ordinal).ToList();
        if (names.Count == 0)
        {
            return new Dictionary<string, AttendancePayloadFormat>(StringComparer.Ordinal);
        }

        var formats = await _catalog.AttendancePayloadFormats.AsNoTracking()
            .Include(f => f.EventCodes)
            .Where(f => names.Contains(f.Name) && !_catalog.AttendancePayloadFormats.Any(newer => newer.SupersedesId == f.Id))
            .ToListAsync(cancellationToken);
        return formats.ToDictionary(f => f.Name, StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<AttendancePayloadFormat>> ListLatestFormatsAsync(CancellationToken cancellationToken) =>
        await _catalog.AttendancePayloadFormats.AsNoTracking()
            .Include(f => f.EventCodes)
            .Where(f => !_catalog.AttendancePayloadFormats.Any(newer => newer.SupersedesId == f.Id))
            .OrderBy(f => f.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AttendanceTransportDefinition>> ListTransportsAsync(CancellationToken cancellationToken) =>
        await _catalog.AttendanceTransportDefinitions.AsNoTracking().OrderBy(t => t.DisplayName).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AttendanceDeviceType>> ListDeviceTypesAsync(CancellationToken cancellationToken) =>
        await _catalog.AttendanceDeviceTypes.AsNoTracking().OrderBy(t => t.Name).ToListAsync(cancellationToken);

    private IQueryable<AttendanceConnectorDefinition> Connectors() =>
        _catalog.AttendanceConnectorDefinitions.AsNoTracking()
            .Include(c => c.Vendor)
            .Include(c => c.DefaultPayloadFormat)
            .Include(c => c.Transports).ThenInclude(t => t.Transport);

    private Dictionary<string, ConnectorInfo> ToInfo(List<AttendanceConnectorDefinition> connectors) =>
        connectors.ToDictionary(
            c => c.ConnectorKey,
            c => new ConnectorInfo(
                c.Id,
                c.ConnectorKey,
                c.DisplayName,
                c.Description,
                c.VendorId,
                c.Vendor!.VendorKey,
                c.Vendor.DisplayName,
                c.Vendor.Manufacturer,
                c.Vendor.WebsiteUrl,
                c.Vendor.IsActive,
                c.IsActive,
                c.AdapterKey,
                c.SupportsIncrementalSync,
                c.EncryptionKind,
                c.DefaultPayloadFormat?.Name,
                c.Transports.Select(t => t.Transport!.TransportKey).OrderBy(k => k, StringComparer.Ordinal).ToList(),
                ConnectorUsability.IsUsable(c, c.Vendor.IsActive, _adapters, _decryptors)),
            StringComparer.Ordinal);
}
