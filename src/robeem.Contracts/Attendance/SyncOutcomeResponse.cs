namespace robeem.Contracts.Attendance;

/// <summary>The outcome of a sync or reconciliation run on request.</summary>
public sealed class SyncOutcomeResponse
{
    /// <summary>Gets what happened: Succeeded, Failed, Skipped or CircuitOpen.</summary>
    public string Status { get; init; } = string.Empty;

    public int? BatchId { get; init; }

    public int RecordsIngested { get; init; }

    public string? Message { get; init; }
}
