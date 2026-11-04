using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Api.Tenancy;
using robeem.Application.Attendance.Catalog;
using robeem.Contracts.Attendance;

namespace robeem.Api.Controllers;

/// <summary>
/// The attendance catalog, read-only, for the other tenants' Apis. This is the one way a tenant other than the prime one reads
/// vendors, connectors, payload formats, transports and device types: it never opens the prime tenant's database. The caller is a
/// service, not a user, so the call carries the catalog service key (<see cref="CatalogServiceKeyAttribute"/>) and no tenant. It
/// always answers from the catalog database, and nothing here can change it.
/// </summary>
[ApiController]
[AllowAnonymous]
[AllowWithoutTenant]
[CatalogServiceKey]
[Route(CatalogProtocol.Route)]
public sealed class CatalogController : ControllerBase
{
    private readonly CatalogReader _catalog;

    public CatalogController(CatalogReader catalog)
    {
        _catalog = catalog;
    }

    [HttpGet("connectors")]
    public async Task<IReadOnlyList<CatalogConnector>> Connectors(CancellationToken cancellationToken) =>
        (await _catalog.ListConnectorsAsync(cancellationToken)).Select(CatalogWireMapper.ToWire).ToList();

    /// <summary>The connectors with these keys (query <c>key</c>, repeated); a key with no connector is left out.</summary>
    [HttpGet("connectors/lookup")]
    public async Task<IReadOnlyList<CatalogConnector>> LookUpConnectors([FromQuery(Name = "key")] string[] keys, CancellationToken cancellationToken) =>
        (await _catalog.GetConnectorsAsync(keys, cancellationToken)).Values.Select(CatalogWireMapper.ToWire).ToList();

    /// <summary>The latest version of every payload format, with its event codes.</summary>
    [HttpGet("payload-formats")]
    public async Task<IReadOnlyList<CatalogPayloadFormat>> PayloadFormats(CancellationToken cancellationToken) =>
        (await _catalog.ListLatestFormatsAsync(cancellationToken)).Select(CatalogWireMapper.ToWire).ToList();

    /// <summary>The latest version of each payload format named (query <c>name</c>, repeated); a name with no format is left out.</summary>
    [HttpGet("payload-formats/lookup")]
    public async Task<IReadOnlyList<CatalogPayloadFormat>> LookUpPayloadFormats([FromQuery(Name = "name")] string[] names, CancellationToken cancellationToken) =>
        (await _catalog.GetLatestFormatsAsync(names, cancellationToken)).Values.Select(CatalogWireMapper.ToWire).ToList();

    /// <summary>One exact version of a payload format: how a batch that recorded it is read again.</summary>
    [HttpGet("payload-formats/version")]
    public async Task<ActionResult<CatalogPayloadFormat>> PayloadFormatVersion(string name, int version, CancellationToken cancellationToken) =>
        await _catalog.GetFormatVersionAsync(name, version, cancellationToken) is { } format ? CatalogWireMapper.ToWire(format) : NotFound();

    [HttpGet("payload-formats/{id:int}")]
    public async Task<ActionResult<CatalogPayloadFormat>> PayloadFormat(int id, CancellationToken cancellationToken) =>
        await _catalog.GetFormatByIdAsync(id, cancellationToken) is { } format ? CatalogWireMapper.ToWire(format) : NotFound();

    [HttpGet("transports")]
    public async Task<IReadOnlyList<CatalogTransport>> Transports(CancellationToken cancellationToken) =>
        (await _catalog.ListTransportsAsync(cancellationToken)).Select(CatalogWireMapper.ToWire).ToList();

    [HttpGet("device-types")]
    public async Task<IReadOnlyList<CatalogDeviceType>> DeviceTypes(CancellationToken cancellationToken) =>
        (await _catalog.ListDeviceTypesAsync(cancellationToken)).Select(CatalogWireMapper.ToWire).ToList();
}
