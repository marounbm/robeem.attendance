using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's exceptions list (<c>api/attendance/exceptions</c>).</summary>
public interface IExceptionsApiClient
{
    /// <param name="status">Active (the default), Open, PendingCorrection, Closed or All.</param>
    /// <param name="take">At most this many, newest first.</param>
    Task<IReadOnlyList<ExceptionListItem>> ListAsync(string? status, DateTime? from, DateTime? to, int? employeeId, int take, CancellationToken cancellationToken);

    /// <summary>Accepts a break exception as it is, with a reason (the employee's manager, or HR). A refusal comes back as the result's errors.</summary>
    Task<ApiSaveResult> AcceptAsync(int exceptionId, AcceptExceptionRequest request, CancellationToken cancellationToken);
}
