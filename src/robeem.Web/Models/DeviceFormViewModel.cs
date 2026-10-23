using Microsoft.AspNetCore.Mvc.ModelBinding;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class DeviceFormViewModel
{
    /// <summary>Gets IANA zones offered as suggestions; any IANA zone id is accepted.</summary>
    public static IReadOnlyList<string> CommonTimeZones => IntegrationDetailsViewModel.CommonTimeZones;

    /// <summary>Gets or sets the device id; null while creating.</summary>
    [BindNever]
    public int? Id { get; set; }

    /// <summary>Gets or sets the integration the device belongs to. Chosen while creating, fixed afterwards.</summary>
    [BindNever]
    public int IntegrationId { get; set; }

    public DeviceInput Input { get; set; } = new();

    /// <summary>Gets or sets the integrations a new device can be added to.</summary>
    [BindNever]
    public IReadOnlyList<IntegrationListItem> Integrations { get; set; } = Array.Empty<IntegrationListItem>();

    /// <summary>Gets or sets the active device types, to choose the device's type from.</summary>
    [BindNever]
    public IReadOnlyList<DeviceTypeOption> DeviceTypes { get; set; } = Array.Empty<DeviceTypeOption>();

    /// <summary>Gets or sets the device as stored; null while creating.</summary>
    [BindNever]
    public DeviceItem? Device { get; set; }

    /// <summary>Gets or sets the device's employee-id mappings. Empty while creating.</summary>
    [BindNever]
    public IReadOnlyList<MappingItem> Mappings { get; set; } = Array.Empty<MappingItem>();

    [BindNever]
    public IReadOnlyList<EmployeeOption> Employees { get; set; } = Array.Empty<EmployeeOption>();
}
