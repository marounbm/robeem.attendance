using Microsoft.EntityFrameworkCore;
using robeem.Application.Common;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceExceptionService : IAttendanceExceptionService
{
    private readonly IRobeemDbContext _db;
    private readonly IAttendanceProcessingEngine _engine;
    private readonly IAttendanceAuditTrail _audit;
    private readonly TimeProvider _clock;

    public AttendanceExceptionService(IRobeemDbContext db, IAttendanceProcessingEngine engine, IAttendanceAuditTrail audit, TimeProvider clock)
    {
        _db = db;
        _engine = engine;
        _audit = audit;
        _clock = clock;
    }

    public async Task<SaveResult> AcceptAsync(int exceptionId, int acceptedByEmployeeId, bool asHr, string? reason, CancellationToken cancellationToken)
    {
        var exception = await _db.AttendanceExceptions.SingleOrDefaultAsync(e => e.Id == exceptionId, cancellationToken);
        if (exception is null)
        {
            return SaveResult.Missing();
        }

        if (!AttendanceExceptionRules.IsAcceptable(exception.ExceptionType))
        {
            return SaveResult.Invalid("id", "Only a break exception, a no-show or a day worked short can be accepted. A missing punch is corrected, with the time it happened.");
        }

        if (exception.Status != AttendanceExceptionStatus.Open)
        {
            return SaveResult.Invalid("id", exception.Status == AttendanceExceptionStatus.Closed ? "This exception is closed." : "This exception is waiting for a correction.");
        }

        if (AttendanceExceptionRules.ReasonProblem(reason, out var trimmed) is { } reasonProblem)
        {
            return reasonProblem;
        }

        var employee = await _db.Profiles.AsNoTracking().SingleAsync(p => p.Id == exception.EmployeeId, cancellationToken);
        var acceptorExists = await _db.Profiles.AnyAsync(p => p.Id == acceptedByEmployeeId, cancellationToken);
        if (AttendanceExceptionRules.AcceptorProblem(employee, acceptorExists, acceptedByEmployeeId, asHr) is { } acceptorProblem)
        {
            return acceptorProblem;
        }

        var role = asHr ? "HR" : "Manager";
        exception.Status = AttendanceExceptionStatus.Closed;
        exception.ResolutionMethod = AttendanceExceptionResolutionMethod.Accepted;
        exception.ResolvedAt = _clock.GetLocalNow().DateTime;
        exception.ResolvedById = acceptedByEmployeeId;
        exception.ResolutionNote = trimmed;
        _audit.Record(
            AttendanceAuditActions.ExceptionStatusChanged,
            nameof(AttendanceException),
            exception.Id,
            $"Open -> Closed: accepted by {role}. Reason: {trimmed}",
            $"employee:{acceptedByEmployeeId}");
        await _db.SaveChangesAsync(cancellationToken);

        // The day no longer has an open exception, so it is calculated again and can be approved.
        await _engine.ProcessEmployeeDayAsync(exception.EmployeeId, exception.AttendanceDate, cancellationToken);
        return SaveResult.Success(exception.Id);
    }
}
