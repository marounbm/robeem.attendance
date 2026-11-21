using Microsoft.EntityFrameworkCore;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>How an exception is shown to a viewer, shared by the flat list and the groups that hold exceptions.</summary>
internal static class ExceptionProjection
{
    /// <summary>Reads the exceptions in the order given, with what the viewer may do with each.</summary>
    public static async Task<List<ExceptionListItem>> ToItemsAsync(IQueryable<AttendanceException> ordered, ExceptionViewer viewer, CancellationToken cancellationToken)
    {
        var viewerId = viewer.EmployeeId;
        var rows = await ordered
            .Select(e => new
            {
                e.Id,
                e.ExceptionGroupId,
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
                ExceptionGroupId = r.ExceptionGroupId,
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
                CanAccept = AttendanceExceptionRules.CanAccept(r.ExceptionType, r.Status, r.EmployeeId, r.ManagerId, viewer),
                ResolutionNote = r.ResolutionNote,
            })
            .ToList();
    }
}
