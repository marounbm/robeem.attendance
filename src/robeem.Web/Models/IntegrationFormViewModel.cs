using Microsoft.AspNetCore.Mvc.ModelBinding;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class IntegrationFormViewModel
{
    /// <summary>Gets or sets the integration id; null while creating.</summary>
    [BindNever]
    public int? Id { get; set; }

    public IntegrationInput Input { get; set; } = new();

    [BindNever]
    public IReadOnlyList<ConnectorOption> Connectors { get; set; } = Array.Empty<ConnectorOption>();

    /// <summary>Gets or sets the active custom file formats a manual-upload integration can be set to read by.</summary>
    [BindNever]
    public IReadOnlyList<PayloadFormatOption> PayloadFormats { get; set; } = Array.Empty<PayloadFormatOption>();

    /// <summary>Gets or sets what each transport declares: the settings and authentication its connection form shows.</summary>
    [BindNever]
    public IReadOnlyList<TransportDescriptorItem> Descriptors { get; set; } = Array.Empty<TransportDescriptorItem>();

    /// <summary>Gets or sets the integration's own devices, one of which can be chosen for uploaded files. Empty while creating.</summary>
    [BindNever]
    public IReadOnlyList<DeviceItem> Devices { get; set; } = Array.Empty<DeviceItem>();
}
