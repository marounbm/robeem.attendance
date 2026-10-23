using Microsoft.EntityFrameworkCore;
using robeem.Application.Common;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceExceptionService : IAttendanceExceptionService
{
    private const int ReasonMaxLength = 1000;

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

        if (exception.ExceptionType is not (AttendanceExceptionType.LongBreak or AttendanceExceptionType.LateBreak or AttendanceExceptionType.EarlyBreak))
        {
            return SaveResult.Invalid("id", "Only a break exception can be accepted. A missing punch is corrected, with the time it happened.");
        }

        if (exception.Status != AttendanceExceptionStatus.Open)
        {
            return SaveResult.Invalid("id", exception.Status == AttendanceExceptionStatus.Closed ? "This exception is closed." : "This exception is waiting for a correction.");
        }

        var trimmed = (reason ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return SaveResult.Invalid(nameof(AcceptExceptionRequest.Reason), "A reason is required to accept an exception.");
        }

        if (trimmed.Length > ReasonMaxLength)
        {
            return SaveResult.Invalid(nameof(AcceptExceptionRequest.Reason), $"The reason can be at most {ReasonMaxLength} characters.");
        }

        var employee = await _db.Profiles.AsNoTracking().SingleAsync(p => p.Id == exception.EmployeeId, cancellationToken);
        if (!await _db.Profiles.AnyAsync(p => p.Id == acceptedByEmployeeId, cancellationToken))
        {
            return SaveResult.Invalid(CorrectionErrorKeys.DecidedBy, "The user does not exist.");
        }

        if (acceptedByEmployeeId == exception.EmployeeId)
        {
            return SaveResult.Invalid(CorrectionErrorKeys.DecidedBy, "An exception can't be accepted by its own employee.");
        }

        // A manager settles their own direct reports'; HR anyone's.
        if (!asHr && employee.ManagerId != acceptedByEmployeeId)
        {
            return SaveResult.Invalid(CorrectionErrorKeys.DecidedBy, "Only the employee's manager, or HR, can accept this exception.");
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
