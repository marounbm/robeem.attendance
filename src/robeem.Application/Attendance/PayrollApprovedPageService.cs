using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Application.Attendance;

public sealed class PayrollApprovedPageService : IPayrollApprovedPageService
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 200;

    private readonly IPayrollAttendanceExtract _extract;

    public PayrollApprovedPageService(IPayrollAttendanceExtract extract)
    {
        _extract = extract;
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

    public async Task<PayrollApprovedPage> GetPageAsync(DateTime from, DateTime to, int page, int pageSize, CancellationToken cancellationToken) =>
        Build(await _extract.GetApprovedRecordsAsync(from, to, cancellationToken), page, pageSize);
}
