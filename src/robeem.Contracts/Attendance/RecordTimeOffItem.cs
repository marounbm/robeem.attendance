namespace robeem.Contracts.Attendance;

/// <summary>A stretch of one day on which the employee is not expected to work: approved leave or a public holiday.</summary>
public sealed class RecordTimeOffItem
{
    /// <summary>Gets what excuses the time: Leave or Holiday.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Gets the leave's type or the holiday's name.</summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>Gets the local start; for a whole day, midnight.</summary>
    public DateTime Start { get; init; }

    /// <summary>Gets the local end; for a whole day, the next midnight.</summary>
    public DateTime End { get; init; }

    /// <summary>Gets a value indicating whether the whole day is excused.</summary>
    public bool IsWholeDay { get; init; }

    /// <summary>Gets the first day of the leave this day belongs to (a leave of several days is one entry per day); null for a holiday.</summary>
    public DateTime? LeaveStartDate { get; init; }

    /// <summary>Gets the last day of the leave, inclusive; null for a holiday.</summary>
    public DateTime? LeaveEndDate { get; init; }

    /// <summary>Gets the note HR wrote with the leave, if any.</summary>
    public string? Notes { get; init; }
}
