using robeem.Application.Common;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>What a user may do with an exception, in one place for every screen that lists them.</summary>
internal static class AttendanceExceptionRules
{
    /// <summary>The longest reason an acceptance can carry.</summary>
    public const int ReasonMaxLength = 1000;

    /// <summary>
    /// Whether the viewer can propose a correction now: it is their own and open, and either a punch is missing (they say when it happened)
    /// or a punch is out of order and at least one other type would settle it.
    /// </summary>
    public static bool CanPropose(AttendanceExceptionType type, AttendanceExceptionStatus status, int employeeId, int viewerId, string suggestedEventTypes) =>
        employeeId == viewerId
        && status == AttendanceExceptionStatus.Open
        && (type == AttendanceExceptionType.MissingPunch
            || (type == AttendanceExceptionType.InvalidSequence && suggestedEventTypes.Length > 0));

    /// <summary>
    /// Whether the viewer can correct the day of the exception: it is still open, whatever it is about, and the day is the viewer's own or
    /// they act for the employee, as their manager or as HR.
    /// </summary>
    public static bool CanCorrect(AttendanceExceptionStatus status, int employeeId, int? managerId, ExceptionViewer viewer) =>
        status == AttendanceExceptionStatus.Open && MayCorrectDayOf(employeeId, managerId, viewer);

    /// <summary>Whether the viewer may correct this employee's days: their own, or on their behalf as their manager or as HR.</summary>
    public static bool MayCorrectDayOf(int employeeId, int? managerId, ExceptionViewer viewer) =>
        employeeId == viewer.EmployeeId || viewer.IsHr || (viewer.IsManager && managerId == viewer.EmployeeId);

    /// <summary>
    /// Whether the viewer can accept the exception as it is: a break exception that is still open, decided by HR or by the employee's
    /// manager, never by the employee.
    /// </summary>
    public static bool CanAccept(AttendanceExceptionType type, AttendanceExceptionStatus status, int employeeId, int? managerId, ExceptionViewer viewer) =>
        IsAcceptable(type)
        && status == AttendanceExceptionStatus.Open
        && employeeId != viewer.EmployeeId
        && (viewer.IsHr || (viewer.IsManager && managerId == viewer.EmployeeId));

    /// <summary>Whether this kind of exception can be accepted as it is: a break exception, a no-show, a day worked short, a late check-in or an early check-out. There is no punch to supply for any of them.</summary>
    public static bool IsAcceptable(AttendanceExceptionType type) =>
        type is AttendanceExceptionType.LongBreak or AttendanceExceptionType.LateBreak or AttendanceExceptionType.EarlyBreak
            or AttendanceExceptionType.NoShow or AttendanceExceptionType.ShortHours
            or AttendanceExceptionType.LateCheckIn or AttendanceExceptionType.EarlyCheckOut
            or AttendanceExceptionType.Overtime or AttendanceExceptionType.UnscheduledWork;

    /// <summary>
    /// Whether the viewer can accept the group of exceptions as it is: it is open, every exception in it that is not closed is open and
    /// of a kind that can be accepted (a single missing punch makes the whole group a correction), and the viewer is HR or the employee's
    /// manager, never the employee.
    /// </summary>
    public static bool CanAcceptGroup(AttendanceExceptionStatus groupStatus, IEnumerable<(AttendanceExceptionType Type, AttendanceExceptionStatus Status)> members, int employeeId, int? managerId, ExceptionViewer viewer)
    {
        var notClosed = members.Where(m => m.Status != AttendanceExceptionStatus.Closed).ToList();
        return groupStatus == AttendanceExceptionStatus.Open
            && notClosed.Count > 0
            && notClosed.All(m => m.Status == AttendanceExceptionStatus.Open && IsAcceptable(m.Type))
            && employeeId != viewer.EmployeeId
            && (viewer.IsHr || (viewer.IsManager && managerId == viewer.EmployeeId));
    }

    /// <summary>Checks the reason given for accepting; null when it is fine, with the trimmed reason in <paramref name="trimmed"/>.</summary>
    public static SaveResult? ReasonProblem(string? reason, out string trimmed)
    {
        trimmed = (reason ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return SaveResult.Invalid(nameof(AcceptExceptionRequest.Reason), "A reason is required to accept an exception.");
        }

        return trimmed.Length > ReasonMaxLength
            ? SaveResult.Invalid(nameof(AcceptExceptionRequest.Reason), $"The reason can be at most {ReasonMaxLength} characters.")
            : null;
    }

    /// <summary>Checks who accepts: an existing user, not the employee, and the employee's manager or HR; null when they may.</summary>
    public static SaveResult? AcceptorProblem(Profile employee, bool acceptorExists, int acceptedByEmployeeId, bool asHr)
    {
        if (!acceptorExists)
        {
            return SaveResult.Invalid(CorrectionErrorKeys.DecidedBy, "The user does not exist.");
        }

        if (acceptedByEmployeeId == employee.Id)
        {
            return SaveResult.Invalid(CorrectionErrorKeys.DecidedBy, "An exception can't be accepted by its own employee.");
        }

        // A manager settles their own direct reports'; HR anyone's.
        return !asHr && employee.ManagerId != acceptedByEmployeeId
            ? SaveResult.Invalid(CorrectionErrorKeys.DecidedBy, "Only the employee's manager, or HR, can accept this exception.")
            : null;
    }

    /// <summary>Splits the stored list of suggested event types.</summary>
    public static IReadOnlyList<string> Suggested(string suggestedEventTypes) =>
        suggestedEventTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
