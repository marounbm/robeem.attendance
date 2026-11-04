using Microsoft.AspNetCore.Mvc.ModelBinding;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>The page that sets a device up from the vendor down: vendor, connector, how it is reached, the device.</summary>
public sealed class DeviceSetupViewModel
{
    public DeviceSetupInput Input { get; set; } = new();

    /// <summary>Gets or sets the vendor chosen in the first step. It only narrows the connectors on the page, so it is not part of the input.</summary>
    public string VendorKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the connectors of the catalog; the page offers the usable ones, narrowed by the vendor.</summary>
    [BindNever]
    public IReadOnlyList<ConnectorOption> Connectors { get; set; } = Array.Empty<ConnectorOption>();

    /// <summary>Gets or sets what each transport declares: the settings and authentication its connection form shows.</summary>
    [BindNever]
    public IReadOnlyList<TransportDescriptorItem> Descriptors { get; set; } = Array.Empty<TransportDescriptorItem>();

    /// <summary>Gets or sets the connections the tenant already has, by connector key: those of the connector's active integration.</summary>
    [BindNever]
    public IReadOnlyDictionary<string, IReadOnlyList<ConnectionListItem>> ConnectionsByConnector { get; set; } = new Dictionary<string, IReadOnlyList<ConnectionListItem>>();

    /// <summary>Gets or sets the active device types, to choose the device's type from.</summary>
    [BindNever]
    public IReadOnlyList<DeviceTypeOption> DeviceTypes { get; set; } = Array.Empty<DeviceTypeOption>();
}
