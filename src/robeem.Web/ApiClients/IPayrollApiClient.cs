using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>The payroll gate: the approved, exception-free days of a period, and nothing else about attendance.</summary>
public interface IPayrollApiClient
{
    /// <summary>The approved days between two dates, both inclusive. A closed pay period yields none.</summary>
    Task<IReadOnlyList<PayrollAttendanceDay>> GetApprovedAsync(DateTime from, DateTime to, CancellationToken cancellationToken);
}
