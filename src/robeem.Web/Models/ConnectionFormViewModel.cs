using Microsoft.AspNetCore.Mvc.ModelBinding;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class ConnectionFormViewModel
{
    /// <summary>Gets or sets the connection id; null while creating.</summary>
    [BindNever]
    public int? Id { get; set; }

    [BindNever]
    public IntegrationListItem Integration { get; set; } = new();

    public ConnectionInput Input { get; set; } = new();

    /// <summary>Gets or sets the transports the integration's connector offers (and file upload), to choose the connection's from.</summary>
    [BindNever]
    public IReadOnlyList<TransportOption> Transports { get; set; } = Array.Empty<TransportOption>();

    /// <summary>Gets or sets what each transport declares: the settings and authentication its form shows.</summary>
    [BindNever]
    public IReadOnlyList<TransportDescriptorItem> Descriptors { get; set; } = Array.Empty<TransportDescriptorItem>();
}
