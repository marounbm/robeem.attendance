using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class AuditIndexViewModel
{
    public const int DefaultTake = 100;

    public static readonly IReadOnlyList<int> TakeChoices = [50, 100, 250, 500];

    public IReadOnlyList<AuditLogEntry> Entries { get; init; } = Array.Empty<AuditLogEntry>();

    public string? Action { get; init; }

    public string? EntityType { get; init; }

    public int? EntityId { get; init; }

    public int Take { get; init; } = DefaultTake;

    public IReadOnlyList<string> Actions => AuditActionCatalog.All;

    /// <summary>Gets a value indicating whether the list is as long as it was allowed to be, so older entries may exist.</summary>
    public bool MayHaveMore => Entries.Count >= Take;
}
