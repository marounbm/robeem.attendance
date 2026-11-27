namespace robeem.Contracts.Attendance;

/// <summary>A pay cycle and the periods it gives around today.</summary>
public sealed class PayCycleItem
{
    public string Frequency { get; init; } = "Monthly";

    public DateTime AnchorDate { get; init; }

    public int StartDay { get; init; } = 1;

    /// <summary>Gets the cycle in words, for example "Monthly, from the 26th".</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets the first day of the period that contains today.</summary>
    public DateTime CurrentStart { get; init; }

    public DateTime CurrentEnd { get; init; }

    /// <summary>Gets the first day of the latest period that has ended.</summary>
    public DateTime LastStart { get; init; }

    public DateTime LastEnd { get; init; }
}
