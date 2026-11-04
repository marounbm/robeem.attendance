namespace robeem.Application.Attendance;

/// <summary>
/// What the review queue is narrowed to, and which page of it is asked for. <paramref name="From"/> and <paramref name="To"/> are
/// inclusive attendance dates. <paramref name="Page"/> is 1-based (a page past the end answers the last page); <paramref name="PageSize"/>
/// is clamped to 1..200 (0 means the default, 20).
/// </summary>
public sealed record ReviewFilter(int? EmployeeId, DateTime? From, DateTime? To, ReviewReason? Reason, int Page, int PageSize);
