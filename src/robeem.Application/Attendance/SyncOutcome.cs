namespace robeem.Application.Attendance;

/// <summary>What one sync or reconciliation of an integration did.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="BatchId">The batch that holds the stored rows, or the failure; null when nothing was stored or recorded.</param>
/// <param name="RecordsIngested">Raw rows stored.</param>
/// <param name="Message">Why it failed or was skipped.</param>
public sealed record SyncOutcome(SyncOutcomeStatus Status, int? BatchId = null, int RecordsIngested = 0, string? Message = null)
{
    public bool Succeeded => Status == SyncOutcomeStatus.Succeeded;
}
