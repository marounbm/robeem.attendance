using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class DeviceTypesIndexViewModel
{
    public IReadOnlyList<DeviceTypeItem> Types { get; init; } = Array.Empty<DeviceTypeItem>();
}
