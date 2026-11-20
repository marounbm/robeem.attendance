namespace robeem.Contracts.Attendance;

/// <summary>What calculating attendance again from a date did.</summary>
public sealed class ReprocessSinceResponse
{
    /// <summary>Gets or sets the first day calculated again.</summary>
    public DateTime From { get; set; }

    /// <summary>Gets or sets the last day calculated again: today.</summary>
    public DateTime To { get; set; }

    /// <summary>Gets or sets how many employee-days were calculated again in this request.</summary>
    public int DaysExamined { get; set; }

    /// <summary>Gets or sets how many of them came out different: a new record version, a retired record, or an exception raised or closed. The others were left as they were.</summary>
    public int DaysChanged { get; set; }

    /// <summary>Gets or sets how many new record versions were written.</summary>
    public int RecordsWritten { get; set; }

    public int RecordsRetired { get; set; }

    /// <summary>Gets or sets how many unchanged records the engine had approved and now need a review, because the policy became stricter or an exception was raised.</summary>
    public int RecordsSentToReview { get; set; }

    /// <summary>Gets or sets how many exceptions were raised, for example because a policy or rule became stricter.</summary>
    public int ExceptionsRaised { get; set; }

    /// <summary>Gets or sets how many open exceptions were closed because their problem is gone.</summary>
    public int ExceptionsResolved { get; set; }

    public int DaysFailed { get; set; }

    /// <summary>Gets or sets how many employee-days were left alone because their pay period is closed.</summary>
    public int DaysInClosedPeriods { get; set; }

    /// <summary>Gets or sets the day to start from to carry on, when one request did not reach them all; null when everything was done.</summary>
    public DateTime? NextFrom { get; set; }
}
