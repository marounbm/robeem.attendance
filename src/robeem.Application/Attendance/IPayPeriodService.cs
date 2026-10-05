using robeem.Application.Common;
using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>
/// Pay periods: the dates payroll pays for, and whether each is closed. Closing a period ends its
/// extraction (the payroll gate returns none of its days); reopening needs a reason. Every change
/// is audited. A stand-in for a Payroll module that does not exist (CLAUDE.md §4).
/// </summary>
public interface IPayPeriodService
{
    /// <summary>Lists the pay periods, latest first.</summary>
    Task<IReadOnlyList<PayPeriodItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Creates an open period. Refused when it ends before it starts, is longer than a year, or overlaps another period.</summary>
    Task<SaveResult> CreateAsync(PayPeriodInput input, CancellationToken cancellationToken);

    /// <summary>Closes an open period; closing a closed one is refused.</summary>
    Task<SaveResult> CloseAsync(int id, CancellationToken cancellationToken);

    /// <summary>Opens a closed period again, with a reason; reopening an open one is refused.</summary>
    Task<SaveResult> ReopenAsync(int id, string? reason, CancellationToken cancellationToken);
}
