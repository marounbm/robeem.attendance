using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceExceptionQuery : IAttendanceExceptionQuery
{
    private const int MaxTake = 500;

    private readonly IRobeemDbContext _db;

    public AttendanceExceptionQuery(IRobeemDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ExceptionListItem>> ListAsync(ExceptionFilter filter, ExceptionViewer viewer, CancellationToken cancellationToken)
    {
        var exceptions = _db.AttendanceExceptions.AsNoTracking();

        // The scope first: HR sees everyone; otherwise the viewer's own and, as a manager, their direct reports'.
        if (!viewer.IsHr)
        {
            var me = viewer.EmployeeId;
            var own = viewer.IsEmployee;
            var reports = viewer.IsManager;
            exceptions = exceptions.Where(e => (own && e.EmployeeId == me) || (reports && e.Employee!.ManagerId == me));
        }

        exceptions = filter.Status switch
        {
            ExceptionStatusFilter.Active => exceptions.Where(e => e.Status != AttendanceExceptionStatus.Closed),
            ExceptionStatusFilter.Open => exceptions.Where(e => e.Status == AttendanceExceptionStatus.Open),
            ExceptionStatusFilter.PendingCorrection => exceptions.Where(e => e.Status == AttendanceExceptionStatus.PendingCorrection),
            ExceptionStatusFilter.Closed => exceptions.Where(e => e.Status == AttendanceExceptionStatus.Closed),
            _ => exceptions,
        };

        if (filter.From is { } from)
        {
            var first = from.Date;
            exceptions = exceptions.Where(e => e.AttendanceDate >= first);
        }

        if (filter.To is { } to)
        {
            var last = to.Date;
            exceptions = exceptions.Where(e => e.AttendanceDate <= last);
        }

        if (filter.EmployeeId is { } employeeId)
        {
            exceptions = exceptions.Where(e => e.EmployeeId == employeeId);
        }

        var viewerId = viewer.EmployeeId;
        var rows = await exceptions
            .OrderByDescending(e => e.AttendanceDate)
            .ThenByDescending(e => e.Id)
            .Take(Math.Clamp(filter.Take, 1, MaxTake))
            .Select(e => new
            {
                e.Id,
                e.EmployeeId,
                e.Employee!.FirstName,
                e.Employee.LastName,
                e.AttendanceDate,
                ShiftTitle = e.TimeScheduleShift != null ? e.TimeScheduleShift.Title : null,
                ShiftStart = e.TimeScheduleShift != null ? e.TimeScheduleShift.StartTime : null,
                ShiftEnd = e.TimeScheduleShift != null ? e.TimeScheduleShift.EndTime : null,
                e.ExceptionType,
                e.MissingEventType,
                e.Status,
                e.Description,
                e.DetectedAt,
                e.ResolvedAt,
                e.ResolutionMethod,
                e.ResolutionNote,
                e.RelatedEventType,
                e.RelatedEventAt,
                e.SuggestedEventTypes,
                ManagerId = e.Employee.ManagerId,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new ExceptionListItem
            {
                Id = r.Id,
                EmployeeId = r.EmployeeId,
                EmployeeName = $"{r.FirstName} {r.LastName}",
                AttendanceDate = r.AttendanceDate,
                ShiftTitle = r.ShiftTitle,
                ShiftIsOvernight = r.ShiftStart is not null && r.ShiftEnd is not null && Set.ParseTime(r.ShiftEnd) < Set.ParseTime(r.ShiftStart),
                ExceptionType = r.ExceptionType.ToString(),
                MissingEventType = r.MissingEventType?.ToString(),
                Status = r.Status.ToString(),
                Description = r.Description,
                DetectedAt = r.DetectedAt,
                ResolvedAt = r.ResolvedAt,
                ResolutionMethod = r.ResolutionMethod?.ToString(),
                RelatedPunchType = r.RelatedEventType?.ToString(),
                RelatedPunchAt = r.RelatedEventAt,
                SuggestedEventTypes = AttendanceExceptionRules.Suggested(r.SuggestedEventTypes),
                CanPropose = AttendanceExceptionRules.CanPropose(r.ExceptionType, r.Status, r.EmployeeId, viewerId, r.SuggestedEventTypes),
                CanCorrect = AttendanceExceptionRules.CanCorrect(r.Status, r.EmployeeId, viewerId),
                CanAccept = r.ExceptionType is AttendanceExceptionType.LongBreak or AttendanceExceptionType.LateBreak or AttendanceExceptionType.EarlyBreak
                    && r.Status == AttendanceExceptionStatus.Open
                    && r.EmployeeId != viewerId
                    && (viewer.IsHr || (viewer.IsManager && r.ManagerId == viewerId)),
                ResolutionNote = r.ResolutionNote,
            })
            .ToList();
    }
}
