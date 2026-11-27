using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's compensation report (<c>api/attendance/payroll/compensation</c>): overtime, under time and penalties per employee and pay period, and the decisions on them.</summary>
public interface ICompensationApiClient
{
    /// <summary>One page of the employees with the totals of their report. <paramref name="mode"/> is Last or Containing.</summary>
    Task<PagedResult<CompensationOverviewItem>> ListAsync(string? search, string mode, DateTime? date, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Every employee of the overview, for the export.</summary>
    Task<IReadOnlyList<CompensationOverviewItem>> ListAllAsync(string? search, string mode, DateTime? date, CancellationToken cancellationToken);

    /// <summary>One employee's report; null when there is no such employee.</summary>
    Task<CompensationReport?> GetReportAsync(int employeeId, string mode, DateTime? date, CancellationToken cancellationToken);

    /// <summary>Records decisions on lines; a refusal says why, and nothing is saved.</summary>
    Task<ApiSaveResult> DecideAsync(CompensationDecisionRequest request, CancellationToken cancellationToken);

    /// <summary>Closes the employee's pay period; the Api refuses while it has not ended or has days that are not ready.</summary>
    Task<ApiSaveResult> CloseAsync(CompensationCloseRequest request, CancellationToken cancellationToken);

    /// <summary>Closes the pay period shown for every ready employee of the overview, and says why each of the others was not closed.</summary>
    Task<CompensationCloseAllResult> CloseAllAsync(CompensationCloseAllRequest request, CancellationToken cancellationToken);

    /// <summary>Opens the employee's closed pay period again; the Api refuses without a reason.</summary>
    Task<ApiSaveResult> ReopenAsync(CompensationReopenRequest request, CancellationToken cancellationToken);
}
