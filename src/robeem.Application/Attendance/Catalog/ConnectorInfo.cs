using robeem.DataModel;

namespace robeem.Application.Attendance.Catalog;

/// <summary>
/// A connector of the catalog together with its vendor, as tenant code needs to see it. A plain record, so it is safe to keep
/// and carries nothing a tenant could save back.
/// </summary>
/// <param name="Id">The connector's id in the catalog. Meaningful only to the catalog's own services; tenant data uses the key.</param>
/// <param name="ConnectorKey">The connector's key, which an integration refers to it by.</param>
/// <param name="DisplayName">The connector's name.</param>
/// <param name="Description">What the connector reads.</param>
/// <param name="VendorId">The vendor's id in the catalog.</param>
/// <param name="VendorKey">The vendor's key: the source system raw punches record.</param>
/// <param name="VendorName">The vendor's name.</param>
/// <param name="VendorManufacturer">The company that makes the devices; empty when not recorded.</param>
/// <param name="VendorWebsiteUrl">The manufacturer's website; empty when not recorded.</param>
/// <param name="VendorIsActive">Whether the vendor is active.</param>
/// <param name="IsActive">Whether the connector is active.</param>
/// <param name="AdapterKey">The key a code adapter is registered under; null for a connector read by payload formats alone.</param>
/// <param name="SupportsIncrementalSync">Whether the source can be asked for what changed after a cursor.</param>
/// <param name="EncryptionKind">How the connector's payload is encrypted.</param>
/// <param name="DefaultPayloadFormatName">The name of the format an upload is read by when the integration names none.</param>
/// <param name="Transports">The keys of the transports the connector supports, besides file upload.</param>
/// <param name="IsUsable">Whether an integration can be made with the connector (<see cref="ConnectorUsability"/>).</param>
public sealed record ConnectorInfo(
    int Id,
    string ConnectorKey,
    string DisplayName,
    string Description,
    int VendorId,
    string VendorKey,
    string VendorName,
    string VendorManufacturer,
    string VendorWebsiteUrl,
    bool VendorIsActive,
    bool IsActive,
    string? AdapterKey,
    bool SupportsIncrementalSync,
    PayloadEncryptionKind EncryptionKind,
    string? DefaultPayloadFormatName,
    IReadOnlyList<string> Transports,
    bool IsUsable);
