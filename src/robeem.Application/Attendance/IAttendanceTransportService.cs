using robeem.Application.Common;
using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>
/// The tenant's names for the six ways attendance reaches robeem. How a transport behaves is code, so the list
/// can't grow from here; a transport can be relabeled, or switched off for new integrations.
/// </summary>
public interface IAttendanceTransportService
{
    Task<IReadOnlyList<TransportItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Gets a transport as editable input, or null when it does not exist.</summary>
    Task<TransportInput?> GetInputAsync(int id, CancellationToken cancellationToken);

    Task<SaveResult> UpdateAsync(int id, TransportInput input, CancellationToken cancellationToken);
}
