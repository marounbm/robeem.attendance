using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// The attendance audit trail (CLAUDE.md §2.8). An entry is staged in the caller's unit of work, so
/// it commits or rolls back with the change it records; the caller saves.
/// </summary>
public interface IAttendanceAuditTrail
{
    /// <summary>Stages an entry for the current user, or for <paramref name="actor"/> when the action is someone else's to name.</summary>
    void Record(string action, string entityType, int? entityId, string? details = null, string? actor = null);

    /// <summary>
    /// Stages an entry for an entity that has no id yet. The id is read when
    /// <see cref="FlushNewAsync"/> runs, after the entity has been saved.
    /// </summary>
    void RecordNew(string action, Entity entity, string? details = null);

    /// <summary>Saves the entries of <see cref="RecordNew"/> now that their entities have ids. Does nothing when there are none.</summary>
    Task FlushNewAsync(CancellationToken cancellationToken);

    /// <summary>Lists entries, newest first. Reading the trail is not itself recorded.</summary>
    Task<IReadOnlyList<AuditLogEntry>> ListAsync(string? action, string? entityType, int? entityId, int take, CancellationToken cancellationToken);
}
