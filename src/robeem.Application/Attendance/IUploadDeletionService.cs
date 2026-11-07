namespace robeem.Application.Attendance;

public enum UploadDeletionOutcome
{
    Deleted,
    NotFound,

    /// <summary>The upload exists but may not be deleted; <see cref="UploadDeletionResult.Reason"/> says why.</summary>
    NotAllowed,
}

/// <summary>
/// Deletes a failed or repeated upload together with its punches, but only while none of them was processed. This is the single
/// exception to the immutability of raw attendance (CLAUDE.md §1.2): a punch that was never read is not attendance data, and it would only
/// sit in the upload for ever. Every deletion is audited, and the audit entry stays.
/// </summary>
public interface IUploadDeletionService
{
    Task<UploadDeletionResult> DeleteAsync(int uploadId, CancellationToken cancellationToken);
}

/// <summary>What became of a request to delete an upload. <see cref="PunchesDeleted"/> is 0 unless it was deleted.</summary>
public sealed record UploadDeletionResult(UploadDeletionOutcome Outcome, string? Reason = null, int PunchesDeleted = 0);
