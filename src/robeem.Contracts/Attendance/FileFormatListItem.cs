namespace robeem.Contracts.Attendance;

/// <summary>A custom file format, as the file formats screen lists it.</summary>
public sealed class FileFormatListItem
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string FileKind { get; init; } = string.Empty;

    public string Delimiter { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public int EventCodeCount { get; init; }

    /// <summary>Gets how many integrations read their uploads by this format.</summary>
    public int IntegrationCount { get; init; }
}
