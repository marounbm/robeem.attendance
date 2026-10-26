using System.Globalization;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class PayrollIndexViewModel
{
    public const int DefaultPageSize = 20;

    /// <summary>Gets the page sizes the user can choose from.</summary>
    public static IReadOnlyList<int> PageSizes { get; } = [10, DefaultPageSize, 50, 100, 200];

    public IReadOnlyList<PayPeriodItem> Periods { get; init; } = Array.Empty<PayPeriodItem>();

    public int? PeriodId { get; init; }

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

    /// <summary>Gets the employee number or name the days are narrowed to; null for everyone.</summary>
    public string? Employee { get; init; }

    /// <summary>Gets the selected pay period, when one was chosen rather than free dates.</summary>
    public PayPeriodItem? Period { get; init; }

    /// <summary>Gets the days of the page payroll may pay for; null until a period or dates were asked for.</summary>
    public IReadOnlyList<PayrollAttendanceDay>? Days { get; init; }

    /// <summary>Gets why nothing could be asked for (an unknown period, dates the wrong way round).</summary>
    public string? Error { get; init; }

    /// <summary>Gets the per-employee totals over every approved day of the range (not only the page), by employee number.</summary>
    public IReadOnlyList<PayrollEmployeeTotal> Employees { get; init; } = Array.Empty<PayrollEmployeeTotal>();

    /// <summary>Gets the 1-based page shown.</summary>
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Gets the number of approved days in the range, on every page together.</summary>
    public int Total { get; init; }

    public bool PeriodIsClosed => Period is { IsClosed: true };

    /// <summary>
    /// Gets a value indicating whether the query is complete: no error, and an employee, or both dates, or both. Payroll
    /// never lists everything, so without either the page only asks for one.
    /// </summary>
    public bool CanQuery => Error is null && (!string.IsNullOrWhiteSpace(Employee) || (From is not null && To is not null));

    /// <summary>Gets what the pager needs: the page, and the pay period or dates that every page link keeps.</summary>
    public PagerModel Pager => new()
    {
        Page = Page,
        PageSize = PageSize,
        Total = Total,
        RouteValues = FilterValues(),
    };

    private Dictionary<string, string?> FilterValues()
    {
        var values = new Dictionary<string, string?> { ["pageSize"] = PageSize.ToString(CultureInfo.InvariantCulture) };
        if (!string.IsNullOrWhiteSpace(Employee))
        {
            values["employee"] = Employee;
        }

        if (PeriodId is { } periodId)
        {
            values["periodId"] = periodId.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            if (From is { } from)
            {
                values["from"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            if (To is { } to)
            {
                values["to"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }

        return values;
    }
}
