using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;

namespace robeem.Application.Attendance;

/// <summary>
/// The date the tenant's punching started (<c>AttendanceSettings.StartDate</c>): no attendance is expected before it. Every place that
/// refuses or skips earlier days reads it here, so the wording and the rule are one.
/// </summary>
public static class AttendanceStartDate
{
    /// <summary>What a punch's error starts with when it is before the start date. Not a failure worth retrying.</summary>
    public const string BeforeStartPrefix = "The punch is before the start date";

    /// <summary>Reads the start date of the current tenant, or null when none is set.</summary>
    public static async Task<DateTime?> ReadAsync(IRobeemDbContext db, CancellationToken cancellationToken)
    {
        var date = await db.AttendanceSettings.AsNoTracking().Select(s => s.StartDate).SingleOrDefaultAsync(cancellationToken);
        return date?.Date;
    }

    /// <summary>
    /// Reads the last day attendance was entered for: the latest day with a calculated day summary. Day summaries are the engine's own,
    /// so this reads no raw or normalized punch and needs no audit entry.
    /// </summary>
    public static async Task<DateTime?> ReadLastAttendanceDateAsync(IRobeemDbContext db, CancellationToken cancellationToken)
    {
        var last = await db.AttendanceDaySummaries.AsNoTracking().MaxAsync(s => (DateTime?)s.AttendanceDate, cancellationToken);
        return last?.Date;
    }

    public static bool IsBefore(DateTime date, DateTime? start) => start is { } s && date.Date < s.Date;

    public static string PunchMessage(DateTime date, DateTime start) =>
        $"{BeforeStartPrefix} ({start:yyyy-MM-dd}): it is on {date:yyyy-MM-dd}. Attendance is not expected before that date, so the punch is kept but not used.";

    public static string DayMessage(DateTime start) =>
        $"Attendance started on {start:yyyy-MM-dd}: nothing can be entered for an earlier day.";
}
