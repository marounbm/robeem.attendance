using robeem.Application.Common;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Application.Attendance;

/// <summary>
/// The compensation report: per employee and per pay period (their own, from their pay cycle), the overtime and under time of the days
/// payroll may pay for, the penalties accumulated, and the payroll administrator's decision on each line. Overtime and under time are
/// not exceptions: they are decided here. Days are read only through <see cref="IPayrollAttendanceExtract"/>, so the gate is unchanged.
/// </summary>
public interface IPayrollCompensationService
{
    /// <summary>The period shown is each employee's most recently finished one, counted from today.</summary>
    public const string LastPeriod = "Last";

    /// <summary>
    /// The period shown is the one that contains each employee's latest day with attendance data, so history that was imported long after it
    /// happened is found; an employee with no attendance at all falls back to <see cref="LastPeriod"/>.
    /// </summary>
    public const string LatestPeriod = "Latest";

    /// <summary>The period shown is the one that contains a given date (today when none is given).</summary>
    public const string ContainingPeriod = "Containing";

    /// <summary>
    /// Lists one page of the active employees (by employee number; the search matches their number or name, ignoring case) with the
    /// totals of their report for <paramref name="mode"/> and <paramref name="date"/>. The page size is 1 to 200 (default 20); a page
    /// past the end answers the last page.
    /// </summary>
    Task<PagedResult<CompensationOverviewItem>> ListAsync(string? search, string? mode, DateTime? date, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Gets every employee of the overview, all pages, for an export (one page at a time underneath).</summary>
    Task<IReadOnlyList<CompensationOverviewItem>> ListAllAsync(string? search, string? mode, DateTime? date, CancellationToken cancellationToken);

    /// <summary>Gets one employee's report; null when there is no such employee.</summary>
    Task<CompensationReport?> GetReportAsync(int employeeId, string? mode, DateTime? date, CancellationToken cancellationToken);

    /// <summary>
    /// Records decisions on lines of one employee's report, all or none. A line must exist in the report and the decision be one it allows;
    /// the period must not be closed. A leave decision adds part-day leaves to the employee's leaves over the time missed at the start and
    /// end of the shifts (paid leave in the chosen type, unpaid leave as Unpaid), which settles the day again; it is refused when no such
    /// time exists or a leave of the employee already overlaps it. Everything is saved in one transaction. The id of the result is the number of decisions.
    /// </summary>
    Task<SaveResult> DecideAsync(CompensationDecisionRequest request, int decidedById, CancellationToken cancellationToken);

    /// <summary>
    /// Closes the employee's pay period. Refused while the period has not ended, while the employee has days with attendance data that are
    /// not ready for payroll (an unsettled exception, a day to review, an unapproved day), and when it is already closed.
    /// </summary>
    Task<SaveResult> ClosePeriodAsync(CompensationCloseRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Closes the pay period shown for every employee of the overview (the same search, mode and date) that is ready: its period has ended,
    /// it has days payroll may pay for and none that is not ready. The others are left as they are and counted with the reason. Each
    /// employee is closed on its own, as <see cref="ClosePeriodAsync"/> does, so one that cannot be closed never holds the rest back.
    /// </summary>
    Task<CompensationCloseAllResult> CloseAllAsync(string? search, string? mode, DateTime? date, CancellationToken cancellationToken);

    /// <summary>Opens the employee's closed pay period again; a reason is required. Refused when the period is not closed.</summary>
    Task<SaveResult> ReopenPeriodAsync(CompensationReopenRequest request, CancellationToken cancellationToken);
}
