namespace robeem.Contracts.Attendance;

/// <summary>The outcome of queueing an integration's punches that failed for want of a time zone.</summary>
public sealed class RetryFailedResponse
{
    /// <summary>Gets how many raw punches were set back to Pending to be normalized again.</summary>
    public int Requeued { get; init; }
}
