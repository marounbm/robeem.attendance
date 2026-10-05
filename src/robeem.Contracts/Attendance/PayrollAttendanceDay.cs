namespace robeem.Contracts.Attendance;

/// <summary>
/// One employee's day that payroll may pay for: approved, exception-free and in an open pay
/// period. The totals are the day summary's; <see cref="Shifts"/> are the current records behind them.
/// </summary>
public sealed record PayrollAttendanceDay(
    int EmployeeId,
    string EmployeeNumber,
    DateTime AttendanceDate,
    TimeSpan TotalWorkingHours,
    TimeSpan TotalOvertimeHours,
    TimeSpan TotalBreakHours,
    IReadOnlyList<PayrollAttendanceShift> Shifts);
