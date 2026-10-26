using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Application.Attendance;

public sealed class PayrollApprovedPageService : IPayrollApprovedPageService
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 200;

    /// <summary>Gets the first and last day read when only an employee is asked for.</summary>
    public static readonly DateTime AllDatesStart = new(1900, 1, 1);

    public static readonly DateTime AllDatesEnd = new(9999, 12, 31);

    private readonly IPayrollAttendanceExtract _extract;

    public PayrollApprovedPageService(IPayrollAttendanceExtract extract)
    {
        _extract = extract;
    }

    /// <summary>Keeps the days of the employees whose number or name contains the text, ignoring case; blank keeps every day (the extract's own filter, in memory).</summary>
    public static IReadOnlyList<PayrollAttendanceDay> Filter(IReadOnlyList<PayrollAttendanceDay> days, string? employee)
    {
        var term = employee?.Trim();
        if (string.IsNullOrEmpty(term))
        {
            return days;
        }

        return days
            .Where(d => d.EmployeeNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                || d.EmployeeName.Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Orders the days by employee number and date, totals them per employee, and cuts one page (a page past the end is the last page).</summary>
    public static PayrollApprovedPage Build(IReadOnlyList<PayrollAttendanceDay> days, int page, int pageSize)
    {
        var size = Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);
        var ordered = days
            .OrderBy(d => d.EmployeeNumber, StringComparer.Ordinal)
            .ThenBy(d => d.EmployeeId)
            .ThenBy(d => d.AttendanceDate)
            .ToList();
        var current = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(ordered.Count / (double)size)));

        var employees = ordered
            .GroupBy(day => (day.EmployeeId, day.EmployeeNumber, day.EmployeeName))
            .Select(group => new PayrollEmployeeTotal(
                group.Key.EmployeeId,
                group.Key.EmployeeNumber,
                group.Key.EmployeeName,
                group.Count(),
                TimeSpan.FromTicks(group.Sum(day => day.TotalWorkingHours.Ticks)),
                TimeSpan.FromTicks(group.Sum(day => day.TotalOvertimeHours.Ticks)),
                TimeSpan.FromTicks(group.Sum(day => day.TotalBreakHours.Ticks))))
            .ToList();

        return new PayrollApprovedPage
        {
            Days = new PagedResult<PayrollAttendanceDay>
            {
                Items = ordered.Skip((current - 1) * size).Take(size).ToList(),
                Total = ordered.Count,
                Page = current,
                PageSize = size,
            },
            Employees = employees,
        };
    }

    /// <summary>
    /// Says why a query is refused, or null when it is allowed. Payroll never reads everything: it takes a period (both dates), an
    /// employee, or both. With an employee and no dates it reads every date of that employee.
    /// </summary>
    public static string? Validate(DateTime? from, DateTime? to, string? employee)
    {
        if (from is null != to is null)
        {
            return "Give both dates, or leave both empty to look at every date of one employee.";
        }

        if (from is null && string.IsNullOrWhiteSpace(employee))
        {
            return "Give an employee, or a pay period or dates.";
        }

        return to < from ? "The period ends before it starts." : null;
    }

    public async Task<PayrollApprovedPage> GetPageAsync(DateTime? from, DateTime? to, string? employee, int page, int pageSize, CancellationToken cancellationToken) =>
        Build(await GetDaysAsync(from, to, employee, cancellationToken), page, pageSize);

    public Task<IReadOnlyList<PayrollAttendanceDay>> GetDaysAsync(DateTime? from, DateTime? to, string? employee, CancellationToken cancellationToken) =>
        Validate(from, to, employee) is { } refusal
            ? throw new ArgumentException(refusal)
            : _extract.GetApprovedRecordsAsync(from ?? AllDatesStart, to ?? AllDatesEnd, employee, cancellationToken);
}
