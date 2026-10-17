using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's device types (<c>api/attendance/device-types</c>).</summary>
public interface IDeviceTypesApiClient
{
    Task<IReadOnlyList<DeviceTypeItem>> ListAsync(CancellationToken cancellationToken);

    Task<ApiSaveResult> CreateAsync(DeviceTypeInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> UpdateAsync(int id, DeviceTypeInput input, CancellationToken cancellationToken);
}
