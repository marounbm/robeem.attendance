using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Application.Attendance;

public interface IAttendanceExceptionGroupQuery
{
    /// <summary>
    /// Lists one page of the exception groups the viewer may see, newest date first, each with its exceptions, and the number that match on
    /// every page together. A page past the end answers the last page.
    /// </summary>
    Task<PagedResult<ExceptionGroupListItem>> ListAsync(ExceptionGroupFilter filter, ExceptionViewer viewer, CancellationToken cancellationToken);
}
