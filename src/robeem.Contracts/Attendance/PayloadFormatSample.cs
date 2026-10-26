namespace robeem.Contracts.Attendance;

/// <summary>An example file made from a payload format as it stands in the form, to show what a file it reads looks like.</summary>
public sealed class PayloadFormatSample
{
    /// <summary>Gets the kind of example: <c>csv</c>, <c>json</c> or <c>xml</c>.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Gets a file name for the example, such as <c>sample-branch-export.json</c>.</summary>
    public string FileName { get; init; } = string.Empty;

    public string ContentType { get; init; } = "text/plain";

    /// <summary>Gets the whole example, as text.</summary>
    public string Content { get; init; } = string.Empty;
}
