namespace robeem.Application.Attendance;

/// <summary>Which exceptions to list, within the viewer's scope. Dates are inclusive and ignore the time of day.</summary>
public sealed record ExceptionFilter(ExceptionStatusFilter Status, DateTime? From, DateTime? To, int? EmployeeId, int Take);
