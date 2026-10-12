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
    public IReadOnlyList<ProviderOption> Providers { get; set; } = Array.Empty<ProviderOption>();

    /// <summary>Gets or sets the active custom file formats a manual-upload integration can be set to read by.</summary>
    [BindNever]
    public IReadOnlyList<FileFormatOption> FileFormats { get; set; } = Array.Empty<FileFormatOption>();

    /// <summary>Gets or sets the integration's own devices, one of which can be chosen for uploaded files. Empty while creating.</summary>
    [BindNever]
    public IReadOnlyList<DeviceItem> Devices { get; set; } = Array.Empty<DeviceItem>();
}
