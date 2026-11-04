namespace robeem.Contracts.Attendance;

/// <summary>A payload format of the catalog with its event codes, as the prime Api sends it for reading a file.</summary>
public sealed class CatalogPayloadFormat
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public int Version { get; init; }

    public int? SupersedesId { get; init; }

    public int? ConnectorId { get; init; }

    /// <summary>Gets the name of an <c>AttendancePayloadKind</c>.</summary>
    public string PayloadKind { get; init; } = string.Empty;

    public string Delimiter { get; init; } = string.Empty;

    public bool HasHeaderRow { get; init; }

    public int SheetNumber { get; init; }

    public string RecordPath { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public string EmployeeColumn { get; init; } = string.Empty;

    public string TimestampColumn { get; init; } = string.Empty;

    public string DateColumn { get; init; } = string.Empty;

    public string TimeColumn { get; init; } = string.Empty;

    public string TypeColumn { get; init; } = string.Empty;

    public string DirectionColumn { get; init; } = string.Empty;

    public string DeviceColumn { get; init; } = string.Empty;

    public string TransactionIdColumn { get; init; } = string.Empty;

    public string TimestampFormats { get; init; } = string.Empty;

    public string DateFormats { get; init; } = string.Empty;

    public string TimeFormats { get; init; } = string.Empty;

    public string TimestampCulture { get; init; } = string.Empty;

    public string PayloadColumns { get; init; } = string.Empty;

    public List<CatalogEventCode> EventCodes { get; init; } = [];
}
