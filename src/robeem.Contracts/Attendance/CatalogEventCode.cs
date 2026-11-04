namespace robeem.Contracts.Attendance;

/// <summary>A label of a payload format and the event type it means.</summary>
public sealed class CatalogEventCode
{
    public int Id { get; init; }

    public string Code { get; init; } = string.Empty;

    /// <summary>Gets the name of a <c>NormalizedEventType</c>.</summary>
    public string EventType { get; init; } = string.Empty;
}
