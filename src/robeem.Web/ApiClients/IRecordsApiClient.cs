using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's calculated attendance (<c>api/attendance/records</c>).</summary>
public interface IRecordsApiClient
{
    /// <param name="status">Calculated, PendingReview, Approved or Rejected; null for any.</param>
    /// <param name="take">At most this many, newest first.</param>
    Task<IReadOnlyList<RecordDayListItem>> ListAsync(DateTime? from, DateTime? to, int? employeeId, string? status, bool onlyWithExceptions, int take, CancellationToken cancellationToken);

    /// <summary>One employee's day in full, or null when there is no such employee or the user may not see them.</summary>
    Task<RecordDayDetail?> GetDayAsync(int employeeId, DateTime date, CancellationToken cancellationToken);

    /// <summary>Runs the day through the processing engine again (HR only); null when there is no such employee.</summary>
    Task<ReprocessResponse?> ReprocessAsync(int employeeId, DateTime date, CancellationToken cancellationToken);
}
