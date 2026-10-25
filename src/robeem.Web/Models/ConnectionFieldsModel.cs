using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>What the shared connection-fields partial draws: the authentication and the settings the chosen transport declares.</summary>
public sealed class ConnectionFieldsModel
{
    /// <summary>Gets the authentication method chosen so far; empty for the transport's usual one.</summary>
    public string? AuthMethod { get; init; }

    /// <summary>Gets the settings entered so far, by key.</summary>
    public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets what each transport declares; the form follows the transport chosen in <see cref="TransportSelectId"/>.</summary>
    public IReadOnlyList<TransportDescriptorItem> Descriptors { get; init; } = [];

    /// <summary>Gets the id of the transport dropdown on the page.</summary>
    public string TransportSelectId { get; init; } = string.Empty;
}
