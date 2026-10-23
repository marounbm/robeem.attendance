using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's calculated attendance (<c>api/attendance/records</c>).</summary>
public interface IRecordsApiClient
{
    /// <param name="status">Calculated, PendingReview, Approved or Rejected; null for any.</param>
    /// <param name="page">The 1-based page of days, newest first.</param>
    /// <param name="pageSize">Days per page.</param>
    Task<PagedResult<RecordDayListItem>> ListAsync(DateTime? from, DateTime? to, int? employeeId, string? status, bool onlyWithExceptions, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Lists the employees whose days the user may see, to filter the list by one of them.</summary>
    Task<IReadOnlyList<EmployeeOption>> ListEmployeeOptionsAsync(CancellationToken cancellationToken);

    /// <summary>One employee's day in full, or null when there is no such employee or the user may not see them.</summary>
    Task<RecordDayDetail?> GetDayAsync(int employeeId, DateTime date, CancellationToken cancellationToken);

    /// <summary>Runs the day through the processing engine again (HR only); null when there is no such employee.</summary>
    Task<ReprocessResponse?> ReprocessAsync(int employeeId, DateTime date, CancellationToken cancellationToken);
}
