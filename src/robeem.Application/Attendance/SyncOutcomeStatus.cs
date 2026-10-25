namespace robeem.Application.Attendance;

public enum SyncOutcomeStatus
{
    /// <summary>The rows are stored and, for an incremental sync, the cursor has moved.</summary>
    Succeeded,

    /// <summary>The vendor or the storage failed, after retries. The cursor is unchanged and the failure is recorded on a batch.</summary>
    Failed,

    /// <summary>The integration's circuit breaker is open: the vendor was not called.</summary>
    CircuitOpen,

    /// <summary>Nothing to do: the integration is missing or not active, or its adapter has no incremental sync.</summary>
    Skipped,
}
