namespace robeem.Application.Attendance;

/// <summary>Which exception groups to list. <see cref="Page"/> is 1-based and <see cref="PageSize"/> 1 to 200 (0 means the default, 20).</summary>
public sealed record ExceptionGroupFilter(ExceptionStatusFilter Status, DateTime? From, DateTime? To, int? EmployeeId, int Page, int PageSize);
