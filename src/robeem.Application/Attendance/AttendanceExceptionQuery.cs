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

        return await ExceptionProjection.ToItemsAsync(
            exceptions
                .OrderByDescending(e => e.AttendanceDate)
                .ThenByDescending(e => e.Id)
                .Take(Math.Clamp(filter.Take, 1, MaxTake)),
            viewer,
            cancellationToken);
    }
}
