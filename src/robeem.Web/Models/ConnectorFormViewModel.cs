using Microsoft.AspNetCore.Mvc.ModelBinding;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class ConnectorFormViewModel
{
    /// <summary>Gets or sets the connector id; null while creating.</summary>
    [BindNever]
    public int? Id { get; set; }

    public ConnectorInput Input { get; set; } = new();

    /// <summary>Gets or sets the vendors a connector can belong to.</summary>
    [BindNever]
    public IReadOnlyList<VendorListItem> Vendors { get; set; } = Array.Empty<VendorListItem>();

    /// <summary>Gets or sets the active payload formats a connector can be given as its default.</summary>
    [BindNever]
    public IReadOnlyList<PayloadFormatOption> PayloadFormats { get; set; } = Array.Empty<PayloadFormatOption>();

    /// <summary>Gets or sets the transports a connector can list.</summary>
    [BindNever]
    public IReadOnlyList<TransportItem> Transports { get; set; } = Array.Empty<TransportItem>();
}
