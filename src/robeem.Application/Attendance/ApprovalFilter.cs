using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// What narrows the approvers' queue, and which page of it. <paramref name="Type"/> keeps one kind; <paramref name="Reason"/> keeps the
/// reviews with that reason (and so no corrections). <paramref name="Page"/> is 1-based; a size of 0 means the default.
/// </summary>
public sealed record ApprovalFilter(int? EmployeeId, DateTime? From, DateTime? To, AttendanceApprovalType? Type, ReviewReason? Reason, int Page, int PageSize);
