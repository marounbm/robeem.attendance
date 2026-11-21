using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceExceptionGroupQuery : IAttendanceExceptionGroupQuery
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 200;

    private readonly IRobeemDbContext _db;

    public AttendanceExceptionGroupQuery(IRobeemDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<ExceptionGroupListItem>> ListAsync(ExceptionGroupFilter filter, ExceptionViewer viewer, CancellationToken cancellationToken)
    {
        var groups = _db.AttendanceExceptionGroups.AsNoTracking();

        // The scope first: HR sees everyone; otherwise the viewer's own and, as a manager, their direct reports'.
        if (!viewer.IsHr)
        {
            var me = viewer.EmployeeId;
            var own = viewer.IsEmployee;
            var reports = viewer.IsManager;
            groups = groups.Where(g => (own && g.EmployeeId == me) || (reports && g.Employee!.ManagerId == me));
        }

        groups = filter.Status switch
        {
            ExceptionStatusFilter.Active => groups.Where(g => g.Status != AttendanceExceptionStatus.Closed),
            ExceptionStatusFilter.Open => groups.Where(g => g.Status == AttendanceExceptionStatus.Open),
            ExceptionStatusFilter.PendingCorrection => groups.Where(g => g.Status == AttendanceExceptionStatus.PendingCorrection),
            ExceptionStatusFilter.Closed => groups.Where(g => g.Status == AttendanceExceptionStatus.Closed),
            _ => groups,
        };

        if (filter.From is { } from)
        {
            var first = from.Date;
            groups = groups.Where(g => g.AttendanceDate >= first);
        }

        if (filter.To is { } to)
        {
            var last = to.Date;
            groups = groups.Where(g => g.AttendanceDate <= last);
        }

        if (filter.EmployeeId is { } employeeId)
        {
            groups = groups.Where(g => g.EmployeeId == employeeId);
        }

        // The total is counted with the same filters as the page; a page past the end is answered with the last page.
        var pageSize = Math.Clamp(filter.PageSize <= 0 ? DefaultPageSize : filter.PageSize, 1, MaxPageSize);
        var total = await groups.CountAsync(cancellationToken);
        var page = Math.Clamp(filter.Page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var rows = await groups
            .OrderByDescending(g => g.AttendanceDate)
            .ThenByDescending(g => g.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(g => new
            {
                g.Id,
                g.EmployeeId,
                g.Employee!.FirstName,
                g.Employee.LastName,
                ManagerId = g.Employee.ManagerId,
                g.AttendanceDate,
                ShiftTitle = g.TimeScheduleShift != null ? g.TimeScheduleShift.Title : null,
                g.Status,
                g.DetectedAt,
                g.ResolvedAt,
                g.ResolutionMethod,
                g.ResolvedById,
                g.ResolutionNote,
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return new PagedResult<ExceptionGroupListItem> { Items = [], Total = total, Page = page, PageSize = pageSize };
        }

        // The exceptions of the groups on the page, and who accepted.
        var ids = rows.Select(r => r.Id).ToList();
        var members = (await ExceptionProjection.ToItemsAsync(
                _db.AttendanceExceptions.AsNoTracking().Where(e => e.ExceptionGroupId != null && ids.Contains(e.ExceptionGroupId.Value)).OrderBy(e => e.Id),
                viewer,
                cancellationToken))
            .ToLookup(e => e.ExceptionGroupId!.Value);

        var resolverIds = rows.Where(r => r.ResolvedById != null).Select(r => r.ResolvedById!.Value).Distinct().ToList();
        var resolvers = resolverIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Profiles.AsNoTracking()
                .Where(p => resolverIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.FirstName + " " + p.LastName, cancellationToken);

        // A correction of a day waits for all of the day's shifts together, so a day with a waiting group is not corrected again.
        var employeeIds = rows.Select(r => r.EmployeeId).Distinct().ToList();
        var firstDay = rows.Min(r => r.AttendanceDate);
        var lastDay = rows.Max(r => r.AttendanceDate);
        var waitingDays = (await _db.AttendanceExceptionGroups.AsNoTracking()
                .Where(g => g.Status == AttendanceExceptionStatus.PendingCorrection
                    && employeeIds.Contains(g.EmployeeId)
                    && g.AttendanceDate >= firstDay
                    && g.AttendanceDate <= lastDay)
                .Select(g => new { g.EmployeeId, g.AttendanceDate })
                .ToListAsync(cancellationToken))
            .Select(g => (g.EmployeeId, g.AttendanceDate.Date))
            .ToHashSet();

        var items = rows.Select(r =>
        {
            var held = members[r.Id].ToList();
            var waiting = r.Status == AttendanceExceptionStatus.PendingCorrection
                || held.Any(e => e.Status == nameof(AttendanceExceptionStatus.PendingCorrection))
                || waitingDays.Contains((r.EmployeeId, r.AttendanceDate.Date));
            return new ExceptionGroupListItem
            {
                Id = r.Id,
                EmployeeId = r.EmployeeId,
                EmployeeName = $"{r.FirstName} {r.LastName}",
                AttendanceDate = r.AttendanceDate,
                ShiftTitle = r.ShiftTitle,
                Status = r.Status.ToString(),
                DetectedAt = r.DetectedAt,
                ResolvedAt = r.ResolvedAt,
                ResolutionMethod = r.ResolutionMethod?.ToString(),
                ResolvedByName = r.ResolvedById is { } by && resolvers.TryGetValue(by, out var name) ? name : null,
                ResolutionNote = r.ResolutionNote,
                ActiveCount = held.Count(e => e.Status != nameof(AttendanceExceptionStatus.Closed)),
                IsWaiting = waiting,
                CanCorrect = !waiting && AttendanceExceptionRules.CanCorrect(r.Status, r.EmployeeId, viewer.EmployeeId),
                CanAccept = !waiting && AttendanceExceptionRules.CanAcceptGroup(
                    r.Status,
                    held.Select(e => (Enum.Parse<AttendanceExceptionType>(e.ExceptionType), Enum.Parse<AttendanceExceptionStatus>(e.Status))),
                    r.EmployeeId,
                    r.ManagerId,
                    viewer),
                Exceptions = held,
            };
        }).ToList();

        return new PagedResult<ExceptionGroupListItem> { Items = items, Total = total, Page = page, PageSize = pageSize };
    }
}
