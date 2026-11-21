using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's exceptions list (<c>api/attendance/exceptions</c>).</summary>
public interface IExceptionsApiClient
{
    /// <param name="status">Active (the default), Open, PendingCorrection, Closed or All.</param>
    /// <param name="take">At most this many, newest first.</param>
    Task<IReadOnlyList<ExceptionListItem>> ListAsync(string? status, DateTime? from, DateTime? to, int? employeeId, int take, CancellationToken cancellationToken);

    /// <summary>
    /// One page of the exception groups (an employee's shift on a date, with its exceptions) the user may see, newest date first.
    /// <paramref name="status"/> is Active (the default), Open, PendingCorrection, Closed or All; a page past the end answers the last page.
    /// </summary>
    Task<PagedResult<ExceptionGroupListItem>> ListGroupsAsync(string? status, DateTime? from, DateTime? to, int? employeeId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Accepts a whole group as it is, with one reason (the employee's manager, or HR). A refusal comes back as the result's errors.</summary>
    Task<ApiSaveResult> AcceptGroupAsync(int groupId, AcceptExceptionRequest request, CancellationToken cancellationToken);

    /// <summary>The employees whose exceptions the user may see (HR everyone, a manager their reports and themselves, an employee themselves), to filter the list by one of them.</summary>
    Task<IReadOnlyList<EmployeeOption>> ListEmployeeOptionsAsync(CancellationToken cancellationToken);

    /// <summary>Accepts a break exception as it is, with a reason (the employee's manager, or HR). A refusal comes back as the result's errors.</summary>
    Task<ApiSaveResult> AcceptAsync(int exceptionId, AcceptExceptionRequest request, CancellationToken cancellationToken);
}
