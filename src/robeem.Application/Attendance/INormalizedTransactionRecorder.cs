using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Stores normalized punches and takes the duplicate decision (CLAUDE.md §1.4). Normalization (P5)
/// adds every normalized row through this, never directly.
/// </summary>
public interface INormalizedTransactionRecorder
{
    /// <summary>
    /// Sets <see cref="NormalizedAttendanceTransaction.DuplicateStatus"/> and saves the row.
    /// <list type="bullet">
    /// <item>When a Unique row with the same duplicate key exists (tenant, employee, device, event
    /// type, <see cref="NormalizedAttendanceTransaction.DuplicateBucketTimestampUtc"/>), the row is
    /// stored as DuplicateConfirmed, pointing at it. Otherwise it is stored as Unique.</item>
    /// <item>A row without an employee is always Unique: punches of unrecognized employees cannot
    /// be told apart.</item>
    /// <item>If a concurrent save wins the race for the key, the row is stored as
    /// DuplicateConfirmed instead of failing.</item>
    /// </list>
    /// The bucket must already be computed. This saves the whole context, so other pending changes
    /// (e.g. the raw row's processing status) are committed with the row. Don't call it inside an
    /// explicit transaction: on PostgreSQL a failed insert aborts it, so the retry could not run.
    /// </summary>
    Task<NormalizedAttendanceTransaction> RecordAsync(NormalizedAttendanceTransaction transaction, CancellationToken cancellationToken = default);
}
