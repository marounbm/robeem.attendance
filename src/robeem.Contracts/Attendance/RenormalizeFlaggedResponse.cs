namespace robeem.Contracts.Attendance;

/// <summary>The outcome of taking an integration's punches that were flagged for an unrecognized device up again.</summary>
public sealed class RenormalizeFlaggedResponse
{
    /// <summary>Gets how many flagged punches were taken out of the calculation and queued to be normalized again.</summary>
    public int Requeued { get; init; }

    /// <summary>Gets how many flagged punches are left because nothing can resolve their device yet.</summary>
    public int Unresolved { get; init; }

    /// <summary>Gets a value indicating whether the integration has a device for uploaded files in force (a manual-upload integration with one chosen).</summary>
    public bool HasDeviceForUploads { get; init; }

    /// <summary>Gets how the integration receives attendance: an AttendanceTransport name such as FileUpload.</summary>
    public string Transport { get; init; } = string.Empty;

    /// <summary>Gets the transport's display name.</summary>
    public string TransportName { get; init; } = string.Empty;
}
