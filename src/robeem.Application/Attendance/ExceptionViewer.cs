namespace robeem.Application.Attendance;

/// <summary>
/// Who is looking at the exceptions and in which roles: an Employee sees their own, a Manager
/// their direct reports', HR everyone's. A user in several roles sees the union.
/// </summary>
public sealed record ExceptionViewer(int EmployeeId, bool IsEmployee, bool IsManager, bool IsHr);
