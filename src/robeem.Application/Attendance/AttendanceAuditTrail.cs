using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Application.Security;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceAuditTrail : IAttendanceAuditTrail
{
    private const int DetailsMaxLength = 2000;
    private const int MaxTake = 500;

    private readonly IRobeemDbContext _db;
    private readonly ICurrentUser _user;
    private readonly TimeProvider _clock;
    private readonly List<(AttendanceAuditLog Entry, Entity Entity)> _pending = [];

    public AttendanceAuditTrail(IRobeemDbContext db, ICurrentUser user, TimeProvider clock)
    {
        _db = db;
        _user = user;
        _clock = clock;
    }

    public void Record(string action, string entityType, int? entityId, string? details = null, string? actor = null) =>
        _db.AttendanceAuditLogs.Add(Create(action, entityType, entityId, details, actor));

    public void RecordNew(string action, Entity entity, string? details = null) =>
        _pending.Add((Create(action, entity.GetType().Name, null, details, null), entity));

    public async Task FlushNewAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        foreach (var (entry, entity) in _pending)
        {
            entry.TargetId = entity.Id;
            _db.AttendanceAuditLogs.Add(entry);
        }

        _pending.Clear();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditLogEntry>> ListAsync(string? action, string? entityType, int? entityId, int take, CancellationToken cancellationToken)
    {
        var entries = _db.AttendanceAuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(action))
        {
            entries = entries.Where(a => a.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            entries = entries.Where(a => a.TargetType == entityType);
        }

        if (entityId is { } id)
        {
            entries = entries.Where(a => a.TargetId == id);
        }

        // Newest first by id: it grows with time and every database can order by it.
        return await entries
            .OrderByDescending(a => a.Id)
            .Take(Math.Clamp(take, 1, MaxTake))
            .Select(a => new AuditLogEntry
            {
                Id = a.Id,
                Actor = a.Actor,
                Action = a.Action,
                EntityType = a.TargetType,
                EntityId = a.TargetId,
                Timestamp = a.Timestamp,
                Details = a.Details,
            })
            .ToListAsync(cancellationToken);
    }

    private AttendanceAuditLog Create(string action, string entityType, int? entityId, string? details, string? actor) => new()
    {
        Actor = actor ?? _user.Actor,
        Action = action,
        TargetType = entityType,
        TargetId = entityId,
        Timestamp = _clock.GetUtcNow(),
        Details = details is { Length: > DetailsMaxLength } ? details[..DetailsMaxLength] : details,
    };
}
