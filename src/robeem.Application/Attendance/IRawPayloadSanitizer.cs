namespace robeem.Application.Attendance;

/// <summary>
/// Removes biometric fields from a vendor payload before it is stored as
/// <c>RawAttendanceTransaction.RawPayload</c> (CLAUDE.md §1.10).
/// </summary>
/// <remarks>
/// This is a backstop. Each vendor adapter must still allowlist the fields it copies; a
/// deny-list cannot catch a template stored under an innocuous name.
/// </remarks>
public interface IRawPayloadSanitizer
{
    /// <summary>
    /// Returns <paramref name="rawJson"/> without any property, at any depth, whose name contains a
    /// denied term. Matching ignores case and non-alphanumeric characters, so <c>Finger_Print</c>
    /// matches <c>fingerprint</c>. A payload with nothing to remove is returned unchanged.
    /// </summary>
    /// <exception cref="FormatException">The payload is not valid JSON, so it cannot be checked.</exception>
    string Sanitize(string rawJson);
}
