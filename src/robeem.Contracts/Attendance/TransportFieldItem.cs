namespace robeem.Contracts.Attendance;

/// <summary>One non-secret setting of a transport.</summary>
public sealed class TransportFieldItem
{
    public string Key { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    /// <summary>Gets the kind of value: <c>text</c>, <c>number</c>, <c>url</c> or <c>path</c>.</summary>
    public string Kind { get; init; } = "text";

    public bool Required { get; init; }

    public string Help { get; init; } = string.Empty;
}
