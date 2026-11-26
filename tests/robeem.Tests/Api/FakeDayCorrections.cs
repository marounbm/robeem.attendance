using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Contracts.Attendance;

namespace robeem.Tests.Api;

/// <summary>The day-correction service for the Api tests that only care who may call it, and as whom.</summary>
internal sealed class FakeDayCorrections : IAttendanceDayCorrectionService
{
    public List<(int EmployeeId, DateTime Date, ExceptionViewer Viewer)> Reads { get; } = [];

    public List<(int EmployeeId, DayCorrectionRequest Request, ExceptionViewer RequestedBy)> Proposals { get; } = [];

    public Task<DayCorrectionView?> GetDayAsync(int employeeId, DateTime date, ExceptionViewer viewer, CancellationToken cancellationToken)
    {
        Reads.Add((employeeId, date, viewer));
        return Task.FromResult<DayCorrectionView?>(new DayCorrectionView { Date = date, EmployeeId = employeeId });
    }

    public Task<SaveResult> ProposeAsync(int employeeId, DayCorrectionRequest request, ExceptionViewer requestedBy, CancellationToken cancellationToken)
    {
        Proposals.Add((employeeId, request, requestedBy));
        return Task.FromResult(SaveResult.Success(1));
    }
}
