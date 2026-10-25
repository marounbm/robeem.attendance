namespace robeem.Contracts.Attendance;

/// <summary>What a transport needs to be configured: the settings it declares and the authentication methods it allows. The Web draws its connection form from this.</summary>
public sealed class TransportDescriptorItem
{
    /// <summary>Gets the transport's key (an <c>AttendanceTransport</c> name).</summary>
    public string Transport { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether robeem fetches from the other end on a schedule.</summary>
    public bool IsPull { get; init; }

    public IReadOnlyList<TransportFieldItem> Fields { get; init; } = [];

    /// <summary>Gets the <c>ConnectionAuthMethod</c> names the transport allows, the usual one first.</summary>
    public IReadOnlyList<string> AuthMethods { get; init; } = [];
}
