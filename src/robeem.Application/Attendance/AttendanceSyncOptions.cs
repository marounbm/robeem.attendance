namespace robeem.Application.Attendance;

/// <summary>Settings of the scheduled synchronization and reconciliation jobs (study §12.4). Bound by the host from <see cref="SectionName"/>.</summary>
public sealed class AttendanceSyncOptions
{
    public const string SectionName = "Attendance:Sync";

    /// <summary>Gets or sets a value indicating whether the sync and reconciliation jobs run at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets how often the sync job looks for integrations that are due, in seconds. Each integration's own cron decides when it runs.</summary>
    public int TickSeconds { get; set; } = 15;

    /// <summary>Gets or sets how far before its stored cursor an incremental sync reads again, in minutes, so that late-arriving punches are not missed. The overlap is absorbed by duplicate detection.</summary>
    public int OverlapMinutes { get; set; } = 5;

    /// <summary>Gets or sets how many times a failed vendor call is retried, with exponential backoff and jitter.</summary>
    public int RetryAttempts { get; set; } = 3;

    /// <summary>Gets or sets the delay before the first retry, in seconds; it doubles each time.</summary>
    public double RetryDelaySeconds { get; set; } = 2;

    /// <summary>Gets or sets how many failed syncs of one integration, within <see cref="BreakerSamplingMinutes"/> and with no success between, open its circuit breaker. At least 2 (a Polly limit).</summary>
    public int BreakerFailureThreshold { get; set; } = 3;

    public int BreakerSamplingMinutes { get; set; } = 60;

    /// <summary>Gets or sets how long an open breaker refuses calls before one probe is allowed, in seconds. At least 0.5.</summary>
    public double BreakerBreakSeconds { get; set; } = 300;

    public bool ReconciliationEnabled { get; set; } = true;

    /// <summary>Gets or sets the schedule of the reconciliation job, as a cron expression in server local time. Default: 02:00 every night.</summary>
    public string ReconciliationCron { get; set; } = "0 2 * * *";

    /// <summary>Gets or sets how far back reconciliation re-pulls, in hours.</summary>
    public int ReconciliationWindowHours { get; set; } = 48;

    /// <summary>Gets or sets how far back the integration health KPIs look, in hours.</summary>
    public int HealthWindowHours { get; set; } = 24;

    /// <summary>Gets or sets how many days a successful sync that delivered nothing is kept, so the health window can count it. Older ones are pruned nightly; 0 keeps them all.</summary>
    public int EmptySyncRetentionDays { get; set; } = 7;
}
