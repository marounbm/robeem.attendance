using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Application.Security;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceAuditTrail : IAttendanceAuditTrail
{
    private const int DetailsMaxLength = 2000;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 200;

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

    public async Task<PagedResult<AuditLogEntry>> ListAsync(string? action, string? entityType, int? entityId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);
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

        // The total is counted with the same filters as the page; a page past the end is answered with the last page.
        var total = await entries.CountAsync(cancellationToken);
        var current = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)size)));

        // Newest first by id: it grows with time, is unique (so pages never overlap) and every database can order by it.
        var rows = await entries
            .OrderByDescending(a => a.Id)
            .Skip((current - 1) * size)
            .Take(size)
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

        return new PagedResult<AuditLogEntry> { Items = rows, Total = total, Page = current, PageSize = size };
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
