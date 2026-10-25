using robeem.Application.Common;
using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>
/// The tenant's catalog of attendance vendors: the companies. A vendor is deactivated, never deleted: connectors and
/// raw punches point at it. How its data arrives and is read is the business of its connectors
/// (<see cref="IAttendanceConnectorService"/>).
/// </summary>
public interface IAttendanceVendorService
{
    Task<IReadOnlyList<VendorListItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Gets a vendor as editable input, or null when it does not exist.</summary>
    Task<VendorInput?> GetInputAsync(int id, CancellationToken cancellationToken);

    Task<SaveResult> CreateAsync(VendorInput input, CancellationToken cancellationToken);

    /// <summary>Updates a vendor. Its key can't change: the raw punches it produced are tied to it.</summary>
    Task<SaveResult> UpdateAsync(int id, VendorInput input, CancellationToken cancellationToken);
}
