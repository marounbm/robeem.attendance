using System.Text;

namespace robeem.Application.Attendance;

/// <summary>
/// What a vendor adapter needs to reach one tenant integration. Built from
/// <c>AttendanceIntegration</c> and the <c>AttendanceConnection</c> it is reached through. <see cref="ConnectionConfigRef"/> is
/// the connection's secret reference, only a reference; the secret it names, when the call needs one, is
/// <see cref="ConnectionSecret"/>.
/// </summary>
public sealed record AttendanceIntegrationConfig(int IntegrationId, string VendorKey, string ConnectionConfigRef)
{
    /// <summary>Gets the connection the call goes through; null when there is none (a manual upload).</summary>
    public int? ConnectionId { get; init; }

    /// <summary>
    /// Gets the connection's non-secret settings (host, base URL, paths), as its transport declares them. Never a credential.
    /// An adapter reads what it needs from here first, and from <see cref="ConnectionSecret"/> only for what predates settings.
    /// </summary>
    public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets the uploaded file to read, for a manual upload.</summary>
    public string? UploadFilePath { get; init; }

    /// <summary>
    /// Gets how far before its stored cursor an incremental sync should read again, so that late
    /// punches are not missed. The cursor is the vendor's own and opaque to the sync job, so the
    /// adapter applies the overlap in its terms (for a time cursor, by subtracting it); one whose
    /// cursor has no notion of time may ignore it. Re-delivered rows are harmless: duplicate
    /// detection absorbs them (CLAUDE.md §1.4). Null when no overlap is asked for.
    /// </summary>
    public TimeSpan? SyncOverlap { get; init; }

    /// <summary>
    /// Gets the secret <see cref="ConnectionConfigRef"/> resolves to (<see cref="Security.ISecretStore"/>),
    /// for the length of one call. Null when the reference is empty or names nothing. Never logged:
    /// it is left out of <see cref="object.ToString"/>.
    /// </summary>
    public string? ConnectionSecret { get; init; }

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"IntegrationId = {IntegrationId}, VendorKey = {VendorKey}, ConnectionConfigRef = {ConnectionConfigRef}");
        return true;
    }
}
