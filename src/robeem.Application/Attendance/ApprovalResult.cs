using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Outcome of deciding an approval. <see cref="Errors"/> is keyed by <see cref="ApprovalErrorKeys"/> or the request's property names.
/// </summary>
public sealed class ApprovalResult
{
    private ApprovalResult(
        int? approvalId,
        AttendanceApprovalType? type,
        AttendanceApprovalStatus? status,
        AttendanceExceptionStatus? exceptionStatus,
        AttendanceRecordStatus? recordStatus,
        bool notFound,
        IReadOnlyDictionary<string, string> errors)
    {
        ApprovalId = approvalId;
        Type = type;
        Status = status;
        ExceptionStatus = exceptionStatus;
        RecordStatus = recordStatus;
        NotFound = notFound;
        Errors = errors;
    }

    public int? ApprovalId { get; }

    public AttendanceApprovalType? Type { get; }

    public AttendanceApprovalStatus? Status { get; }

    /// <summary>Gets the status of the exception a correction waited for, once it has been decided. Null for a review.</summary>
    public AttendanceExceptionStatus? ExceptionStatus { get; }

    /// <summary>Gets the status of the reviewed record once it has been decided. Null for a correction.</summary>
    public AttendanceRecordStatus? RecordStatus { get; }

    public bool NotFound { get; }

    public IReadOnlyDictionary<string, string> Errors { get; }

    public bool Succeeded => !NotFound && Errors.Count == 0;

    public static ApprovalResult Correction(AttendanceApproval approval, AttendanceExceptionStatus exceptionStatus) =>
        new(approval.Id, approval.Type, approval.Status, exceptionStatus, null, false, new Dictionary<string, string>());

    public static ApprovalResult Review(AttendanceApproval approval, AttendanceRecordStatus recordStatus) =>
        new(approval.Id, approval.Type, approval.Status, null, recordStatus, false, new Dictionary<string, string>());

    public static ApprovalResult Missing() => new(null, null, null, null, null, true, new Dictionary<string, string>());

    public static ApprovalResult Invalid(string field, string message) =>
        new(null, null, null, null, null, false, new Dictionary<string, string> { [field] = message });
}
