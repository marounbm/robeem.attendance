using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using robeem.Application.Attendance;
using robeem.Application.Attendance.Catalog;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Infrastructure.Attendance.Catalog;

/// <inheritdoc cref="IRemoteCatalogReader" />
public sealed class RemoteCatalogReader : IRemoteCatalogReader
{
    private readonly HttpClient _http;
    private readonly IAttendanceAdapterCatalog _adapters;
    private readonly IPayloadDecryptorCatalog _decryptors;

    public RemoteCatalogReader(HttpClient http, IAttendanceAdapterCatalog adapters, IPayloadDecryptorCatalog decryptors)
    {
        _http = http;
        _adapters = adapters;
        _decryptors = decryptors;
    }

    public async Task<ConnectorInfo?> GetConnectorAsync(string connectorKey, CancellationToken cancellationToken) =>
        (await GetConnectorsAsync([connectorKey], cancellationToken)).GetValueOrDefault(connectorKey);

    public async Task<IReadOnlyDictionary<string, ConnectorInfo>> GetConnectorsAsync(IEnumerable<string> connectorKeys, CancellationToken cancellationToken)
    {
        var keys = connectorKeys.Distinct(StringComparer.Ordinal).ToList();
        if (keys.Count == 0)
        {
            return new Dictionary<string, ConnectorInfo>(StringComparer.Ordinal);
        }

        var wire = await GetAsync<List<CatalogConnector>>($"connectors/lookup{Query("key", keys)}", cancellationToken) ?? [];
        return wire.Select(c => CatalogWireMapper.ToInfo(c, _adapters, _decryptors)).ToDictionary(c => c.ConnectorKey, StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<ConnectorInfo>> ListConnectorsAsync(CancellationToken cancellationToken)
    {
        var wire = await GetAsync<List<CatalogConnector>>("connectors", cancellationToken) ?? [];
        return wire.Select(c => CatalogWireMapper.ToInfo(c, _adapters, _decryptors)).ToList();
    }

    public async Task<AttendancePayloadFormat?> GetLatestFormatAsync(string formatName, CancellationToken cancellationToken) =>
        (await GetLatestFormatsAsync([formatName], cancellationToken)).GetValueOrDefault(formatName);

    public async Task<AttendancePayloadFormat?> GetFormatVersionAsync(string formatName, int version, CancellationToken cancellationToken) =>
        await GetAsync<CatalogPayloadFormat>($"payload-formats/version?name={Uri.EscapeDataString(formatName)}&version={version}", cancellationToken, missingIsNull: true) is { } wire
            ? CatalogWireMapper.ToFormat(wire)
            : null;

    public async Task<AttendancePayloadFormat?> GetFormatByIdAsync(int id, CancellationToken cancellationToken) =>
        await GetAsync<CatalogPayloadFormat>($"payload-formats/{id}", cancellationToken, missingIsNull: true) is { } wire
            ? CatalogWireMapper.ToFormat(wire)
            : null;

    public async Task<IReadOnlyDictionary<string, AttendancePayloadFormat>> GetLatestFormatsAsync(IEnumerable<string> formatNames, CancellationToken cancellationToken)
    {
        var names = formatNames.Distinct(StringComparer.Ordinal).ToList();
        if (names.Count == 0)
        {
            return new Dictionary<string, AttendancePayloadFormat>(StringComparer.Ordinal);
        }

        var wire = await GetAsync<List<CatalogPayloadFormat>>($"payload-formats/lookup{Query("name", names)}", cancellationToken) ?? [];
        return wire.Select(CatalogWireMapper.ToFormat).ToDictionary(f => f.Name, StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<AttendancePayloadFormat>> ListLatestFormatsAsync(CancellationToken cancellationToken) =>
        (await GetAsync<List<CatalogPayloadFormat>>("payload-formats", cancellationToken) ?? []).Select(CatalogWireMapper.ToFormat).ToList();

    public async Task<IReadOnlyList<AttendanceTransportDefinition>> ListTransportsAsync(CancellationToken cancellationToken) =>
        (await GetAsync<List<CatalogTransport>>("transports", cancellationToken) ?? []).Select(CatalogWireMapper.ToTransport).ToList();

    public async Task<IReadOnlyList<AttendanceDeviceType>> ListDeviceTypesAsync(CancellationToken cancellationToken) =>
        (await GetAsync<List<CatalogDeviceType>>("device-types", cancellationToken) ?? []).Select(CatalogWireMapper.ToDeviceType).ToList();

    private static string Query(string name, IEnumerable<string> values) =>
        "?" + string.Join('&', values.Select(v => $"{name}={Uri.EscapeDataString(v)}"));

    /// <summary>
    /// One call to the prime Api. A 404 is "there is none" (null); anything else that is not a success, a timeout, a refused
    /// connection or an unreadable answer is a <see cref="CatalogUnavailableException"/> that names the cause and never the key or the other end's body.
    /// </summary>
    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken, bool missingIsNull = false)
        where T : class
    {
        try
        {
            using var response = await _http.GetAsync(path, cancellationToken);
            if (missingIsNull && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new CatalogUnavailableException($"The prime tenant's Api answered {(int)response.StatusCode} when asked for the attendance catalog.");
            }

            return await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions.Web, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new CatalogUnavailableException("The prime tenant's Api could not be reached for the attendance catalog.", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CatalogUnavailableException("The prime tenant's Api did not answer in time for the attendance catalog.", ex);
        }
        catch (JsonException ex)
        {
            throw new CatalogUnavailableException("The prime tenant's Api sent an attendance catalog this version cannot read.", ex);
        }
    }
}
