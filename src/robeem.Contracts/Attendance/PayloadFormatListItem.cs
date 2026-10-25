namespace robeem.Contracts.Attendance;

/// <summary>A custom file format, as the file formats screen lists it.</summary>
public sealed class PayloadFormatListItem
{
    public int Id { get; init; }

    /// <summary>Gets the version of the format shown: a change to how files are read, once punches were read by it, is a new version.</summary>
    public int Version { get; init; } = 1;

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string PayloadKind { get; init; } = string.Empty;

    public string Delimiter { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public int EventCodeCount { get; init; }

    /// <summary>Gets how many integrations read their uploads by this format.</summary>
    public int IntegrationCount { get; init; }
}
