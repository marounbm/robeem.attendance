namespace robeem.Contracts.Attendance;

public sealed class AuditLogEntry
{
    public int Id { get; init; }

    public string Actor { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string EntityType { get; init; } = string.Empty;

    public int? EntityId { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public string? Details { get; init; }
}
