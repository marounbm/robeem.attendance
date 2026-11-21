using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Keeps the groups of an employee's day in step with the day's exceptions. An exception with no group is put in the not-closed group of
/// its shift (one is made when there is none), and every group's status is taken from its exceptions: Open while any is open, else
/// PendingCorrection while one waits for a correction, else Closed. A group that is already closed is left as it is, so that who accepted
/// it and why are kept. It never changes an exception. Calling it again changes nothing.
/// </summary>
internal static class ExceptionGroupSync
{
    /// <summary>
    /// Brings the day's groups in line with its exceptions and stages the changes; the caller saves, and flushes the audit trail's new entries
    /// afterwards. <paramref name="added"/> are exceptions the caller has added but not saved yet.
    /// </summary>
    public static async Task ApplyAsync(IRobeemDbContext db, IAttendanceAuditTrail audit, int employeeId, DateTime date, IReadOnlyCollection<AttendanceException> added, CancellationToken cancellationToken)
    {
        var day = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
        var exceptions = await db.AttendanceExceptions
            .Where(e => e.EmployeeId == employeeId && e.AttendanceDate == day)
            .ToListAsync(cancellationToken);
        exceptions.AddRange(added);

        var groups = await db.AttendanceExceptionGroups
            .Where(g => g.EmployeeId == employeeId && g.AttendanceDate == day)
            .ToListAsync(cancellationToken);
        var created = new HashSet<AttendanceExceptionGroup>();

        // An exception with no group goes to the group of its shift that is not closed; a closed one, to a closed group.
        foreach (var exception in exceptions.Where(e => e.ExceptionGroup is null && e.ExceptionGroupId is null).OrderBy(e => e.Id))
        {
            var closed = exception.Status == AttendanceExceptionStatus.Closed;
            var group = groups.FirstOrDefault(g => g.TimeScheduleShiftId == exception.TimeScheduleShiftId && (g.Status == AttendanceExceptionStatus.Closed) == closed);
            if (group is null)
            {
                group = new AttendanceExceptionGroup
                {
                    EmployeeId = employeeId,
                    AttendanceDate = day,
                    TimeScheduleShiftId = exception.TimeScheduleShiftId,
                    Status = exception.Status,
                    DetectedAt = exception.DetectedAt,
                };
                db.AttendanceExceptionGroups.Add(group);
                groups.Add(group);
                created.Add(group);
            }

            exception.ExceptionGroup = group;
        }

        foreach (var group in groups)
        {
            var members = exceptions.Where(e => ReferenceEquals(e.ExceptionGroup, group) || (!created.Contains(group) && e.ExceptionGroupId == group.Id)).ToList();
            if (members.Count == 0)
            {
                continue;
            }

            var isNew = created.Contains(group);
            var before = group.Status;
            var derived = Derive(members);
            if (!isNew && before == derived)
            {
                // Unchanged; a group closed by its own decision keeps its resolver and reason.
                continue;
            }

            group.Status = derived;
            if (isNew)
            {
                group.DetectedAt = members.Min(m => m.DetectedAt);
            }

            if (derived == AttendanceExceptionStatus.Closed)
            {
                // The group closes the way its last exception did.
                var last = members.Where(m => m.ResolvedAt != null).OrderBy(m => m.ResolvedAt).ThenBy(m => m.Id).LastOrDefault() ?? members[^1];
                (group.ResolvedAt, group.ResolutionMethod, group.ResolvedById, group.ResolutionNote) = (last.ResolvedAt, last.ResolutionMethod, last.ResolvedById, last.ResolutionNote);
            }
            else
            {
                (group.ResolvedAt, group.ResolutionMethod, group.ResolvedById, group.ResolutionNote) = (null, null, null, null);
            }

            if (isNew)
            {
                audit.RecordNew(AttendanceAuditActions.ExceptionGroupStatusChanged, group, $"(new) -> {derived}: {members.Count} exception(s) of the shift.");
            }
            else
            {
                audit.Record(AttendanceAuditActions.ExceptionGroupStatusChanged, nameof(AttendanceExceptionGroup), group.Id, $"{before} -> {derived}: the group follows its exceptions.");
            }
        }
    }

    /// <summary>Brings the day's groups in line with its exceptions and saves, for a caller that has just changed an exception's status.</summary>
    public static async Task SyncAndSaveAsync(IRobeemDbContext db, IAttendanceAuditTrail audit, int employeeId, DateTime date, CancellationToken cancellationToken)
    {
        await ApplyAsync(db, audit, employeeId, date, [], cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.FlushNewAsync(cancellationToken);
    }

    private static AttendanceExceptionStatus Derive(IReadOnlyCollection<AttendanceException> members) =>
        members.Any(m => m.Status == AttendanceExceptionStatus.Open) ? AttendanceExceptionStatus.Open
        : members.Any(m => m.Status == AttendanceExceptionStatus.PendingCorrection) ? AttendanceExceptionStatus.PendingCorrection
        : AttendanceExceptionStatus.Closed;
}
