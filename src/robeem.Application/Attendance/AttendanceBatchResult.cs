namespace robeem.Application.Attendance;

/// <summary>
/// The transactions retrieved in one provider call, plus the cursor to resume from (incremental
/// sync only).
/// </summary>
public sealed record AttendanceBatchResult(IReadOnlyList<ExternalAttendanceTransaction> Transactions, SyncCursor? NextCursor = null);
