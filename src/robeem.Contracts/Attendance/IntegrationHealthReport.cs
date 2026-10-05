namespace robeem.Contracts.Attendance;

/// <summary>
/// The health of one attendance integration (study §17.3): its stored state, the circuit breaker,
/// and the six KPIs over a recent window. A rate is null when nothing happened in the window to
/// measure it against.
/// </summary>
public sealed class IntegrationHealthReport
{
    public int IntegrationId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string ProviderKey { get; init; } = string.Empty;

    /// <summary>Gets the integration's configured status: Active, Paused or Failed.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets the stored health: Unknown, Healthy, Degraded or Unhealthy. Kept by the sync job.</summary>
    public string HealthStatus { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the circuit breaker is refusing calls to the provider right now. In memory: a restart closes it.</summary>
    public bool IsCircuitOpen { get; init; }

    public DateTime? LastSuccessfulSyncAt { get; init; }

    public DateTime? LastFailureAt { get; init; }

    public string? LastFailureMessage { get; init; }

    /// <summary>Gets the start of the window the KPIs cover, as local wall-clock time. It ends now.</summary>
    public DateTime WindowStart { get; init; }

    public DateTime WindowEnd { get; init; }

    /// <summary>Gets the number of synchronizations recorded in the window, quiet ones included. Meaningful for windows within the empty-sync retention (default 7 days).</summary>
    public int Syncs { get; init; }

    public int FailedSyncs { get; init; }

    /// <summary>Gets the share of recorded synchronizations that did not fail, 0 to 1.</summary>
    public double? SyncSuccessRate { get; init; }

    public int TransactionsReceived { get; init; }

    public int TransactionsFailed { get; init; }

    /// <summary>Gets the share of received transactions that could not be processed, 0 to 1.</summary>
    public double? FailedTransactionRate { get; init; }

    public int TransactionsNormalized { get; init; }

    public int Duplicates { get; init; }

    /// <summary>Gets the share of normalized transactions that are duplicates, 0 to 1.</summary>
    public double? DuplicateRate { get; init; }

    /// <summary>Gets the number of employee-days with punches in the window.</summary>
    public int EmployeeDays { get; init; }

    public int MissingPunchExceptions { get; init; }

    /// <summary>Gets the missing-punch exceptions per employee-day with punches, 0 to 1.</summary>
    public double? MissingEventRate { get; init; }

    /// <summary>Gets the average time from a punch to its receipt, in seconds. Null when no transaction carried a time.</summary>
    public double? AverageSyncDelaySeconds { get; init; }

    /// <summary>Gets the exceptions not closed yet, of the days this integration delivered punches for.</summary>
    public int UnresolvedExceptions { get; init; }
}
