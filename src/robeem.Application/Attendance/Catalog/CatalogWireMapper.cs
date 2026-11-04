using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance.Catalog;

/// <summary>
/// Turns the catalog into what the prime Api sends and back into what <see cref="ICatalogReader"/> hands tenant code. The wire
/// carries plain data, never an entity; enums travel by name, and a name this build does not know is a
/// <see cref="CatalogUnavailableException"/> rather than a guess.
/// </summary>
public static class CatalogWireMapper
{
    public static CatalogConnector ToWire(ConnectorInfo connector) => new()
    {
        Id = connector.Id,
        ConnectorKey = connector.ConnectorKey,
        DisplayName = connector.DisplayName,
        Description = connector.Description,
        VendorId = connector.VendorId,
        VendorKey = connector.VendorKey,
        VendorName = connector.VendorName,
        VendorManufacturer = connector.VendorManufacturer,
        VendorWebsiteUrl = connector.VendorWebsiteUrl,
        VendorIsActive = connector.VendorIsActive,
        IsActive = connector.IsActive,
        AdapterKey = connector.AdapterKey,
        SupportsIncrementalSync = connector.SupportsIncrementalSync,
        EncryptionKind = connector.EncryptionKind.ToString(),
        DefaultPayloadFormatName = connector.DefaultPayloadFormatName,
        Transports = [.. connector.Transports],
    };

    /// <summary>Reads a connector. Usability is decided here, with this process's own adapters and decryptors, not the prime Api's.</summary>
    public static ConnectorInfo ToInfo(CatalogConnector wire, IAttendanceAdapterCatalog adapters, IPayloadDecryptorCatalog decryptors)
    {
        var encryption = ParseEnum<PayloadEncryptionKind>(wire.EncryptionKind);
        return new ConnectorInfo(
            wire.Id,
            wire.ConnectorKey,
            wire.DisplayName,
            wire.Description,
            wire.VendorId,
            wire.VendorKey,
            wire.VendorName,
            wire.VendorManufacturer,
            wire.VendorWebsiteUrl,
            wire.VendorIsActive,
            wire.IsActive,
            wire.AdapterKey,
            wire.SupportsIncrementalSync,
            encryption,
            wire.DefaultPayloadFormatName,
            wire.Transports,
            ConnectorUsability.IsUsable(wire.IsActive, wire.VendorIsActive, encryption, wire.DefaultPayloadFormatName is not null, wire.AdapterKey, adapters, decryptors));
    }

    public static CatalogPayloadFormat ToWire(AttendancePayloadFormat format) => new()
    {
        Id = format.Id,
        Name = format.Name,
        Description = format.Description,
        Version = format.Version,
        SupersedesId = format.SupersedesId,
        ConnectorId = format.ConnectorId,
        PayloadKind = format.PayloadKind.ToString(),
        Delimiter = format.Delimiter,
        HasHeaderRow = format.HasHeaderRow,
        SheetNumber = format.SheetNumber,
        RecordPath = format.RecordPath,
        IsActive = format.IsActive,
        EmployeeColumn = format.EmployeeColumn,
        TimestampColumn = format.TimestampColumn,
        DateColumn = format.DateColumn,
        TimeColumn = format.TimeColumn,
        TypeColumn = format.TypeColumn,
        DirectionColumn = format.DirectionColumn,
        DeviceColumn = format.DeviceColumn,
        TransactionIdColumn = format.TransactionIdColumn,
        TimestampFormats = format.TimestampFormats,
        DateFormats = format.DateFormats,
        TimeFormats = format.TimeFormats,
        TimestampCulture = format.TimestampCulture,
        PayloadColumns = format.PayloadColumns,
        EventCodes = format.EventCodes.Select(c => new CatalogEventCode { Id = c.Id, Code = c.Code, EventType = c.EventType.ToString() }).ToList(),
    };

    /// <summary>A detached format, as the database reader hands it out, for reading a file by.</summary>
    public static AttendancePayloadFormat ToFormat(CatalogPayloadFormat wire)
    {
        var format = new AttendancePayloadFormat
        {
            Id = wire.Id,
            Name = wire.Name,
            Description = wire.Description,
            Version = wire.Version,
            SupersedesId = wire.SupersedesId,
            ConnectorId = wire.ConnectorId,
            PayloadKind = ParseEnum<AttendancePayloadKind>(wire.PayloadKind),
            Delimiter = wire.Delimiter,
            HasHeaderRow = wire.HasHeaderRow,
            SheetNumber = wire.SheetNumber,
            RecordPath = wire.RecordPath,
            IsActive = wire.IsActive,
            EmployeeColumn = wire.EmployeeColumn,
            TimestampColumn = wire.TimestampColumn,
            DateColumn = wire.DateColumn,
            TimeColumn = wire.TimeColumn,
            TypeColumn = wire.TypeColumn,
            DirectionColumn = wire.DirectionColumn,
            DeviceColumn = wire.DeviceColumn,
            TransactionIdColumn = wire.TransactionIdColumn,
            TimestampFormats = wire.TimestampFormats,
            DateFormats = wire.DateFormats,
            TimeFormats = wire.TimeFormats,
            TimestampCulture = wire.TimestampCulture,
            PayloadColumns = wire.PayloadColumns,
        };
        foreach (var code in wire.EventCodes)
        {
            format.EventCodes.Add(new AttendancePayloadEventCode
            {
                Id = code.Id,
                PayloadFormatId = wire.Id,
                Code = code.Code,
                EventType = ParseEnum<NormalizedEventType>(code.EventType),
            });
        }

        return format;
    }

    public static CatalogTransport ToWire(AttendanceTransportDefinition transport) => new()
    {
        Id = transport.Id,
        TransportKey = transport.TransportKey,
        DisplayName = transport.DisplayName,
        IsPull = transport.IsPull,
        UsesPayloadFormat = transport.UsesPayloadFormat,
        IsActive = transport.IsActive,
    };

    public static AttendanceTransportDefinition ToTransport(CatalogTransport wire) => new()
    {
        Id = wire.Id,
        TransportKey = wire.TransportKey,
        DisplayName = wire.DisplayName,
        IsPull = wire.IsPull,
        UsesPayloadFormat = wire.UsesPayloadFormat,
        IsActive = wire.IsActive,
    };

    public static CatalogDeviceType ToWire(AttendanceDeviceType type) => new() { Id = type.Id, Name = type.Name, IsActive = type.IsActive };

    public static AttendanceDeviceType ToDeviceType(CatalogDeviceType wire) => new() { Id = wire.Id, Name = wire.Name, IsActive = wire.IsActive };

    private static T ParseEnum<T>(string name)
        where T : struct, Enum =>
        Enum.TryParse<T>(name, out var value) && Enum.IsDefined(value)
            ? value
            : throw new CatalogUnavailableException($"The prime catalog sent '{name}', which this version does not know as a {typeof(T).Name}.");
}
