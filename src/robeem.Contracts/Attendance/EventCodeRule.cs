namespace robeem.Contracts.Attendance;

/// <summary>One event label a vendor sends and the normalized event type it becomes.</summary>
public sealed class EventCodeRule
{
    public string Code { get; init; } = string.Empty;

    public string EventType { get; init; } = string.Empty;
}
