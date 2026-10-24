using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class PayrollIndexViewModel
{
    public IReadOnlyList<PayPeriodItem> Periods { get; init; } = Array.Empty<PayPeriodItem>();

    public int? PeriodId { get; init; }

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

    /// <summary>Gets the selected pay period, when one was chosen rather than free dates.</summary>
    public PayPeriodItem? Period { get; init; }

    /// <summary>Gets the days payroll may pay for; null until a period or dates were asked for.</summary>
    public IReadOnlyList<PayrollAttendanceDay>? Days { get; init; }

    /// <summary>Gets why nothing could be asked for (an unknown period, dates the wrong way round).</summary>
    public string? Error { get; init; }

    /// <summary>Gets the per-employee totals of <see cref="Days"/>, by employee number.</summary>
    public IReadOnlyList<PayrollEmployeeTotal> Employees =>
        (Days ?? [])
            .GroupBy(day => (day.EmployeeId, day.EmployeeNumber, day.EmployeeName))
            .Select(group => new PayrollEmployeeTotal(
                group.Key.EmployeeId,
                group.Key.EmployeeNumber,
                group.Key.EmployeeName,
                group.Count(),
                TimeSpan.FromTicks(group.Sum(day => day.TotalWorkingHours.Ticks)),
                TimeSpan.FromTicks(group.Sum(day => day.TotalOvertimeHours.Ticks)),
                TimeSpan.FromTicks(group.Sum(day => day.TotalBreakHours.Ticks))))
            .OrderBy(total => total.EmployeeNumber, StringComparer.Ordinal)
            .ToList();

    public bool PeriodIsClosed => Period is { IsClosed: true };
}
