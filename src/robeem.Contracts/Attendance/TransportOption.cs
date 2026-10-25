namespace robeem.Contracts.Attendance;

/// <summary>One way attendance reaches robeem, as an integration chooses it.</summary>
public sealed class TransportOption
{
    /// <summary>Gets the AttendanceTransport member name, e.g. FileUpload. What an integration stores.</summary>
    public string Key { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether robeem fetches the attendance itself, on a schedule.</summary>
    public bool IsPull { get; init; }

    /// <summary>Gets a value indicating whether the attendance arrives as a file read by a file format.</summary>
    public bool UsesPayloadFormat { get; init; }

    /// <summary>Gets a value indicating whether a new integration can choose it. An integration that already has it keeps it.</summary>
    public bool IsActive { get; init; }
}
