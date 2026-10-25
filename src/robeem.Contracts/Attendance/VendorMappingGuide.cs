namespace robeem.Contracts.Attendance;

/// <summary>How one vendor's raw data becomes normalized data: its columns, timestamp formats and event codes.</summary>
public sealed class VendorMappingGuide
{
    public string VendorKey { get; init; } = string.Empty;

    /// <summary>Gets a one-paragraph description of the source and how it is read.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Gets the file extensions, with the dot, that the adapter reads from an upload. Empty for a vendor that reads no files.</summary>
    public IReadOnlyList<string> FileExtensions { get; init; } = [];

    /// <summary>Gets where each vendor field goes.</summary>
    public IReadOnlyList<FieldMappingRule> Fields { get; init; } = [];

    /// <summary>Gets the timestamp formats the vendor's interpreter accepts.</summary>
    public IReadOnlyList<string> TimestampFormats { get; init; } = [];

    /// <summary>Gets the vendor's event labels and the event type each one becomes. Anything else fails the raw row.</summary>
    public IReadOnlyList<EventCodeRule> EventCodes { get; init; } = [];

    /// <summary>Gets the vendor fields that are deliberately never stored.</summary>
    public IReadOnlyList<string> NotStored { get; init; } = [];
}
