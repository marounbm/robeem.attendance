using Microsoft.EntityFrameworkCore;
using robeem.Application.Common;
using robeem.Application.Persistence;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceExceptionGroupService : IAttendanceExceptionGroupService
{
    private readonly IRobeemDbContext _db;
    private readonly IAttendanceProcessingEngine _engine;
    private readonly IAttendanceAuditTrail _audit;
    private readonly TimeProvider _clock;

    public AttendanceExceptionGroupService(IRobeemDbContext db, IAttendanceProcessingEngine engine, IAttendanceAuditTrail audit, TimeProvider clock)
    {
        _db = db;
        _engine = engine;
        _audit = audit;
        _clock = clock;
    }

    public Task SyncDayAsync(int employeeId, DateTime date, CancellationToken cancellationToken) =>
        ExceptionGroupSync.SyncAndSaveAsync(_db, _audit, employeeId, date, cancellationToken);

    public async Task<SaveResult> AcceptAsync(int groupId, int acceptedByEmployeeId, bool asHr, string? reason, CancellationToken cancellationToken)
    {
        var group = await _db.AttendanceExceptionGroups.SingleOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        if (group is null)
        {
            return SaveResult.Missing();
        }

        if (group.Status != AttendanceExceptionStatus.Open)
        {
            return SaveResult.Invalid("id", group.Status == AttendanceExceptionStatus.Closed ? "This group is closed." : "This group is waiting for a correction.");
        }

        var members = await _db.AttendanceExceptions
            .Where(e => e.ExceptionGroupId == group.Id && e.Status != AttendanceExceptionStatus.Closed)
            .OrderBy(e => e.Id)
            .ToListAsync(cancellationToken);
        if (members.Count == 0)
        {
            return SaveResult.Invalid("id", "This group has no open exception.");
        }

        if (members.Any(e => e.Status == AttendanceExceptionStatus.PendingCorrection))
        {
            return SaveResult.Invalid("id", "A correction of this shift is waiting for a decision.");
        }

        if (members.Any(e => !AttendanceExceptionRules.IsAcceptable(e.ExceptionType)))
        {
            return SaveResult.Invalid("id", "This group has a problem that can't be accepted as it is, such as a missing punch, which is corrected with the time it happened. Correct the day instead.");
        }

        if (AttendanceExceptionRules.ReasonProblem(reason, out var trimmed) is { } reasonProblem)
        {
            return reasonProblem;
        }

        var employee = await _db.Profiles.AsNoTracking().SingleAsync(p => p.Id == group.EmployeeId, cancellationToken);
        var acceptorExists = await _db.Profiles.AnyAsync(p => p.Id == acceptedByEmployeeId, cancellationToken);
        if (AttendanceExceptionRules.AcceptorProblem(employee, acceptorExists, acceptedByEmployeeId, asHr) is { } acceptorProblem)
        {
            return acceptorProblem;
        }

        var role = asHr ? "HR" : "Manager";
        var actor = $"employee:{acceptedByEmployeeId}";
        var now = _clock.GetLocalNow().DateTime;
        foreach (var exception in members)
        {
            exception.Status = AttendanceExceptionStatus.Closed;
            exception.ResolutionMethod = AttendanceExceptionResolutionMethod.Accepted;
            exception.ResolvedAt = now;
            exception.ResolvedById = acceptedByEmployeeId;
            exception.ResolutionNote = trimmed;
            _audit.Record(
                AttendanceAuditActions.ExceptionStatusChanged,
                nameof(AttendanceException),
                exception.Id,
                $"Open -> Closed: accepted with its group by {role}. Reason: {trimmed}",
                actor);
        }

        group.Status = AttendanceExceptionStatus.Closed;
        group.ResolutionMethod = AttendanceExceptionResolutionMethod.Accepted;
        group.ResolvedAt = now;
        group.ResolvedById = acceptedByEmployeeId;
        group.ResolutionNote = trimmed;
        _audit.Record(
            AttendanceAuditActions.ExceptionGroupStatusChanged,
            nameof(AttendanceExceptionGroup),
            group.Id,
            $"Open -> Closed: accepted by {role} ({members.Count} exception(s)). Reason: {trimmed}",
            actor);
        await _db.SaveChangesAsync(cancellationToken);

        // The day no longer has an open exception, so it is calculated again and can be approved.
        await _engine.ProcessEmployeeDayAsync(group.EmployeeId, group.AttendanceDate, cancellationToken);
        return SaveResult.Success(group.Id);
    }
}
