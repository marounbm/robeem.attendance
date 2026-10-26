using robeem.Contracts.Common;

namespace robeem.Contracts.Attendance;

/// <summary>
/// One page of the approved days payroll may pay for, with the totals per employee over all of the days
/// (not only those on the page), so the totals do not change as the page does.
/// </summary>
public sealed class PayrollApprovedPage
{
    /// <summary>Gets one page of the days, by employee number and then date.</summary>
    public PagedResult<PayrollAttendanceDay> Days { get; init; } = new();

    /// <summary>Gets the totals of every employee that has an approved day in the range, by employee number.</summary>
    public IReadOnlyList<PayrollEmployeeTotal> Employees { get; init; } = Array.Empty<PayrollEmployeeTotal>();
}
