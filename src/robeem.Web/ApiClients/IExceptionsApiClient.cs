using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's exceptions list (<c>api/attendance/exceptions</c>).</summary>
public interface IExceptionsApiClient
{
    /// <param name="status">Active (the default), Open, PendingCorrection, Closed or All.</param>
    /// <param name="take">At most this many, newest first.</param>
    Task<IReadOnlyList<ExceptionListItem>> ListAsync(string? status, DateTime? from, DateTime? to, int? employeeId, int take, CancellationToken cancellationToken);
}
