namespace robeem.Contracts.Attendance;

/// <summary>One employee's totals over the approved days of a payroll query.</summary>
public sealed record PayrollEmployeeTotal(
    int EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    int Days,
    TimeSpan WorkingHours,
    TimeSpan OvertimeHours,
    TimeSpan BreakHours);
