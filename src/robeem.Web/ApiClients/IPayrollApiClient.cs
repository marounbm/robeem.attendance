using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>The payroll gate: the approved, exception-free days of a period, and nothing else about attendance.</summary>
public interface IPayrollApiClient
{
    /// <summary>All the approved days between two dates, both inclusive; for the CSV. A closed pay period yields none.</summary>
    Task<IReadOnlyList<PayrollAttendanceDay>> GetApprovedAsync(DateTime from, DateTime to, CancellationToken cancellationToken);

    /// <summary>One page of the same days for the screen, with the totals per employee over all of them.</summary>
    Task<PayrollApprovedPage> GetApprovedPageAsync(DateTime from, DateTime to, int page, int pageSize, CancellationToken cancellationToken);
}
