using System.Text;

namespace robeem.Application.Attendance;

/// <summary>
/// What a provider adapter needs to reach one tenant integration. Built from
/// <c>AttendanceIntegration</c>. <see cref="ConnectionConfigRef"/> is only a reference; the secret
/// it names, when the call needs one, is <see cref="ConnectionSecret"/>.
/// </summary>
public sealed record AttendanceIntegrationConfig(int IntegrationId, string ProviderKey, string ConnectionConfigRef)
{
    /// <summary>Gets the uploaded file to read, for a manual upload.</summary>
    public string? UploadFilePath { get; init; }

    /// <summary>
    /// Gets how far before its stored cursor an incremental sync should read again, so that late
    /// punches are not missed. The cursor is the provider's own and opaque to the sync job, so the
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
        builder.Append($"IntegrationId = {IntegrationId}, ProviderKey = {ProviderKey}, ConnectionConfigRef = {ConnectionConfigRef}");
        return true;
    }
}
