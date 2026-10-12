namespace robeem.Contracts.Attendance;

/// <summary>An active custom file format an integration can be set to read its uploads by.</summary>
public sealed class FileFormatOption
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;
}
