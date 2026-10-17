using robeem.Application.Common;
using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>The kinds of punch device a tenant defines. A type is deactivated, never deleted: devices point at it.</summary>
public interface IAttendanceDeviceTypeService
{
    Task<IReadOnlyList<DeviceTypeItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Lists the active types, for choosing a device's type.</summary>
    Task<IReadOnlyList<DeviceTypeOption>> ListOptionsAsync(CancellationToken cancellationToken);

    /// <summary>Gets a type as editable input, or null when it does not exist.</summary>
    Task<DeviceTypeInput?> GetInputAsync(int id, CancellationToken cancellationToken);

    Task<SaveResult> CreateAsync(DeviceTypeInput input, CancellationToken cancellationToken);

    Task<SaveResult> UpdateAsync(int id, DeviceTypeInput input, CancellationToken cancellationToken);
}
