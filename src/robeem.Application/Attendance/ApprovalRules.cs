using robeem.Application.Persistence;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// The rules of the one approval workflow, shared by corrections and reviews: how an approval is made, who may take which step,
/// when it is complete, and which approvals each kind of approver sees. Kept in one place so the two kinds cannot drift apart.
/// </summary>
internal static class ApprovalRules
{
    public static ApprovalWorkflow ToWorkflow(CorrectionApprovalWorkflow workflow) => workflow switch
    {
        CorrectionApprovalWorkflow.Manager => ApprovalWorkflow.Manager,
        CorrectionApprovalWorkflow.HR => ApprovalWorkflow.HR,
        _ => ApprovalWorkflow.Both,
    };

    /// <summary>A pending approval for a request to correct a day; its edit rows point to it.</summary>
    public static AttendanceApproval NewCorrectionApproval(int employeeId, DateTime day, int requestedByEmployeeId, DateTime now, CorrectionApprovalWorkflow workflow) => new()
    {
        Type = AttendanceApprovalType.Correction,
        EmployeeId = employeeId,
        AttendanceDate = day,
        Workflow = ToWorkflow(workflow),
        RequestedByEmployeeId = requestedByEmployeeId,
        RequestedAt = now,
    };

    /// <summary>A pending approval for a record the engine left <c>PendingReview</c>; one decision by the manager or HR.</summary>
    public static AttendanceApproval NewReviewApproval(AttendanceRecord record, DateTime now) => new()
    {
        Type = AttendanceApprovalType.Review,
        EmployeeId = record.EmployeeId,
        AttendanceDate = record.AttendanceDate,
        Workflow = ApprovalWorkflow.ManagerOrHr,
        RequestedAt = now,
        AttendanceRecord = record,
        OvertimeMinutes = record.OvertimeMinutes,
    };

    /// <summary>An employee with no manager, or whose manager is inactive: HR decides for them.</summary>
    public static bool IsUnmanaged(Profile employee) => employee.ManagerId is null || employee.Manager is not { IsActive: true };

    /// <summary>Why this role can't take the approval's next step, or null when it can.</summary>
    public static string? RoleProblem(AttendanceApproval approval, bool isHrDecision) => approval.Workflow switch
    {
        ApprovalWorkflow.Manager when isHrDecision => "This correction needs the manager's decision only.",
        ApprovalWorkflow.HR when !isHrDecision => "This correction needs HR's decision only.",
        ApprovalWorkflow.Both when isHrDecision && approval.ManagerDecision is null => "The manager decides first, then HR.",
        ApprovalWorkflow.Both when !isHrDecision && approval.ManagerDecision is not null => "The manager has already decided.",
        _ => null,
    };

    /// <summary>
    /// Whether an approving decision completes the workflow: the manager for Manager, HR for HR and for Both (the manager decides
    /// first), and the one decision of a review.
    /// </summary>
    public static bool IsComplete(AttendanceApproval approval, bool decidesAsHr) => approval.Workflow switch
    {
        ApprovalWorkflow.Manager => !decidesAsHr,
        ApprovalWorkflow.ManagerOrHr => true,
        _ => decidesAsHr,
    };

    /// <summary>Pending correction requests whose next step is the manager's, for this manager's direct reports.</summary>
    public static IQueryable<AttendanceApproval> CorrectionsAwaitingManager(IQueryable<AttendanceApproval> approvals, int managerEmployeeId) =>
        approvals.Where(a => a.Type == AttendanceApprovalType.Correction
            && a.Status == AttendanceApprovalStatus.Pending
            && a.ManagerDecision == null
            && (a.Workflow == ApprovalWorkflow.Manager || a.Workflow == ApprovalWorkflow.Both)
            && a.Employee!.ManagerId == managerEmployeeId);

    /// <summary>
    /// Pending correction requests whose next step is HR's: those that ask for HR, those the manager has approved, and the manager-step
    /// ones of employees with no manager or an inactive one, which nobody else can decide.
    /// </summary>
    public static IQueryable<AttendanceApproval> CorrectionsAwaitingHr(IQueryable<AttendanceApproval> approvals) =>
        approvals.Where(a => a.Type == AttendanceApprovalType.Correction
            && a.Status == AttendanceApprovalStatus.Pending
            && (a.Workflow == ApprovalWorkflow.HR
                || (a.Workflow == ApprovalWorkflow.Both && a.ManagerDecision == AttendanceCorrectionDecision.Approved)
                || (a.ManagerDecision == null
                    && a.Workflow != ApprovalWorkflow.HR
                    && (a.Employee!.ManagerId == null || a.Employee.Manager!.IsActive == false))));

    /// <summary>
    /// Pending reviews this approver may decide: not their own, of a record that is still current, with no exception of that employee,
    /// date and shift left open (it comes first), and of a direct report unless the approver is HR.
    /// </summary>
    public static IQueryable<AttendanceApproval> ReviewsFor(IRobeemDbContext db, IQueryable<AttendanceApproval> approvals, int approverEmployeeId, bool isHr) =>
        approvals.Where(a => a.Type == AttendanceApprovalType.Review
            && a.Status == AttendanceApprovalStatus.Pending
            && a.EmployeeId != approverEmployeeId
            && a.AttendanceRecord!.IsCurrent
            && !db.AttendanceExceptions.Any(e => e.EmployeeId == a.EmployeeId
                && e.AttendanceDate == a.AttendanceDate
                && e.TimeScheduleShiftId == a.AttendanceRecord.TimeScheduleShiftId
                && e.Status != AttendanceExceptionStatus.Closed)
            && (isHr || a.Employee!.ManagerId == approverEmployeeId));

    /// <summary>Why a record needs review, the first that applies. The filter in the approval query says the same thing: keep the two in step.</summary>
    public static ReviewReason ReasonOf(int overtimeMinutes, int? shiftId, DateTime? checkIn, DateTime? checkOut) =>
        overtimeMinutes > 0 ? ReviewReason.Overtime
        : shiftId is null ? ReviewReason.UnscheduledWork
        : checkIn is null || checkOut is null ? ReviewReason.IncompleteDay
        : ReviewReason.Review;
}
