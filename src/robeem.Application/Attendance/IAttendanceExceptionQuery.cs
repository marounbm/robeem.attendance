using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

public interface IAttendanceExceptionQuery
{
    /// <summary>Lists the exceptions the viewer may see, newest date first, at most <see cref="ExceptionFilter.Take"/> (1 to 500).</summary>
    Task<IReadOnlyList<ExceptionListItem>> ListAsync(ExceptionFilter filter, ExceptionViewer viewer, CancellationToken cancellationToken);
}
