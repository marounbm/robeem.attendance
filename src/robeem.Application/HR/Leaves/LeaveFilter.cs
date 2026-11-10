namespace robeem.Application.HR.Leaves;

/// <summary>
/// What a page of leaves is filtered by. <paramref name="From"/> and <paramref name="To"/> keep the leaves that overlap that range;
/// <paramref name="Search"/> matches the employee's number or name, ignoring case. <paramref name="Page"/> is 1-based.
/// </summary>
public sealed record LeaveFilter(int? EmployeeId, string? Search, DateTime? From, DateTime? To, string? LeaveType, int Page, int PageSize);
