using Microsoft.AspNetCore.Mvc.ModelBinding;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class ProviderFormViewModel
{
    /// <summary>Gets or sets the provider id; null while creating.</summary>
    [BindNever]
    public int? Id { get; set; }

    public ProviderInput Input { get; set; } = new();

    /// <summary>Gets or sets the active file formats a provider can be given as its default.</summary>
    [BindNever]
    public IReadOnlyList<FileFormatOption> FileFormats { get; set; } = Array.Empty<FileFormatOption>();

    /// <summary>Gets or sets the transports a provider can list.</summary>
    [BindNever]
    public IReadOnlyList<TransportItem> Transports { get; set; } = Array.Empty<TransportItem>();
}
