namespace robeem.Contracts.Attendance;

/// <summary>The first rows of a sample file as a format would read them. Nothing is stored.</summary>
public sealed class FileFormatPreview
{
    /// <summary>Gets a reason the file could not be read at all; empty when it was.</summary>
    public string Problem { get; init; } = string.Empty;

    /// <summary>Gets how many data rows the file has.</summary>
    public int TotalRows { get; init; }

    public IReadOnlyList<FileFormatPreviewRow> Rows { get; init; } = [];
}
