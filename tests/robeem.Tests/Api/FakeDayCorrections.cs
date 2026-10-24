using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Contracts.Attendance;

namespace robeem.Tests.Api;

/// <summary>The day-correction service for the Api tests that only care who may call it, and as whom.</summary>
internal sealed class FakeDayCorrections : IAttendanceDayCorrectionService
{
    public List<(int EmployeeId, DateTime Date, int Viewer)> Reads { get; } = [];

    public List<(int EmployeeId, DayCorrectionRequest Request, int RequestedBy)> Proposals { get; } = [];

    public Task<DayCorrectionView?> GetDayAsync(int employeeId, DateTime date, int viewerEmployeeId, CancellationToken cancellationToken)
    {
        Reads.Add((employeeId, date, viewerEmployeeId));
        return Task.FromResult<DayCorrectionView?>(new DayCorrectionView { Date = date, EmployeeId = employeeId });
    }

    public Task<SaveResult> ProposeAsync(int employeeId, DayCorrectionRequest request, int requestedByEmployeeId, CancellationToken cancellationToken)
    {
        Proposals.Add((employeeId, request, requestedByEmployeeId));
        return Task.FromResult(SaveResult.Success(1));
    }
}
