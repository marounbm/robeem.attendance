namespace robeem.Contracts.Attendance;

/// <summary>What looking for missing days did.</summary>
public sealed class CheckMissingDaysResponse
{
    /// <summary>Gets or sets the first day looked at.</summary>
    public DateTime From { get; set; }

    /// <summary>Gets or sets the last day looked at: at most yesterday.</summary>
    public DateTime To { get; set; }

    /// <summary>Gets or sets how many employee-days were calculated.</summary>
    public int DaysCalculated { get; set; }

    public int DaysFailed { get; set; }

    /// <summary>Gets or sets how many more employee-days there are than one request calculates; ask again for them.</summary>
    public int DaysDeferred { get; set; }

    /// <summary>Gets or sets the dates nobody punched on at all: these are skipped, because it is the import that is missing, not everyone who was away.</summary>
    public List<DateTime> DatesWithoutData { get; set; } = [];
}
