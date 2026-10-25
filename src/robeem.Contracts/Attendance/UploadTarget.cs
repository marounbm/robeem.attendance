namespace robeem.Contracts.Attendance;

/// <summary>
/// An integration a file can be uploaded into: where the punches go (the integration), the format
/// the file must have (its vendor's file types) and the layout the vendor reads it by.
/// </summary>
public sealed class UploadTarget
{
    public int IntegrationId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string VendorKey { get; init; } = string.Empty;

    public string VendorName { get; init; } = string.Empty;

    /// <summary>Gets the file extensions the vendor reads, with the dot, e.g. <c>.xls</c>.</summary>
    public IReadOnlyList<string> FileExtensions { get; init; } = [];

    /// <summary>Gets the layout of the file: its columns, timestamp formats and event labels, as the adapter reads them.</summary>
    public VendorMappingGuide Layout { get; init; } = new();
}
