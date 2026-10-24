using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>The zone a punch proposed by an employee is in, for every kind of correction that names a time.</summary>
internal static class CorrectionTimeZones
{
    /// <summary>
    /// The zone the proposed local time is in: that of the employee's punch nearest to it, as that
    /// device's clock is the one the schedule is compared with; the server's zone when the employee
    /// has no punch around then.
    /// </summary>
    public static async Task<string> ResolveAsync(
        IRobeemDbContext db,
        IAttendanceAuditTrail audit,
        TimeProvider clock,
        int employeeId,
        DateTime proposed,
        string actor,
        CancellationToken cancellationToken)
    {
        // A direct read of normalized attendance outside the engine: audited, and saved at once, since
        // a proposal that is then refused saves nothing else.
        audit.Record(AttendanceAuditActions.AttendanceDataRead, nameof(NormalizedAttendanceTransaction), null, $"Time zone for a correction of employee {employeeId}, near {proposed:s}.", actor);
        await db.SaveChangesAsync(cancellationToken);

        var nearby = await db.NormalizedAttendanceTransactions
            .AsNoTracking()
            .Where(n => n.EmployeeId == employeeId
                && n.EventTimestampLocal >= proposed.AddDays(-1)
                && n.EventTimestampLocal < proposed.AddDays(1))
            .Select(n => new { n.EventTimestampLocal, n.TimeZoneId })
            .ToListAsync(cancellationToken);

        return nearby.Count == 0
            ? clock.LocalTimeZone.Id
            : nearby.MinBy(n => (n.EventTimestampLocal - proposed).Duration())!.TimeZoneId;
    }
}
