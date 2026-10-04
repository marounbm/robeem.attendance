using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendancePolicyResolver : IAttendancePolicyResolver
{
    private readonly IRobeemDbContext _db;

    public AttendancePolicyResolver(IRobeemDbContext db)
    {
        _db = db;
    }

    public async Task<AttendancePolicy> ResolveAsync(int? employeeId, CancellationToken cancellationToken)
    {
        int? scheduleId = null;
        if (employeeId is not null)
        {
            scheduleId = await _db.TimeScheduleEmployees
                .Where(e => e.EmployeeId == employeeId)
                .Select(e => (int?)e.TimeScheduleId)
                .SingleOrDefaultAsync(cancellationToken);
        }

        // At most one policy per schedule and one default (filtered unique indexes), so ordering
        // the schedule's own policy first is enough.
        var policy = await _db.AttendancePolicies
            .AsNoTracking()
            .Where(p => p.TimeScheduleId == null || (scheduleId != null && p.TimeScheduleId == scheduleId))
            .OrderBy(p => p.TimeScheduleId == null)
            .FirstOrDefaultAsync(cancellationToken);

        return policy ?? new AttendancePolicy { Title = "Built-in default" };
    }
}
