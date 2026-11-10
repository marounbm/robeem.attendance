namespace robeem.Application.HR.Absence;

/// <summary>Why a stretch of time is excused.</summary>
public enum ExcusedKind
{
    Leave,
    Holiday,
}

/// <summary>
/// A stretch of time on which an employee is not expected to work. A whole day runs from midnight
/// to the next midnight; a part-day leave carries its own local wall-clock <paramref name="Start"/>
/// and <paramref name="End"/> (the end may fall on the next day).
/// </summary>
/// <param name="Kind">Leave or public holiday.</param>
/// <param name="Reference">The holiday's name or the leave's type, for a note.</param>
/// <param name="Start">Local wall-clock start.</param>
/// <param name="End">Local wall-clock end (exclusive).</param>
/// <param name="IsWholeDay">True for a whole day; false for part of one.</param>
public sealed record ExcusedPeriod(ExcusedKind Kind, string Reference, DateTime Start, DateTime End, bool IsWholeDay)
{
    /// <summary>Gets the date a whole-day period excuses.</summary>
    public DateTime Date => Start.Date;

    /// <summary>Gets a value indicating whether this period overlaps <paramref name="from"/> (inclusive) and <paramref name="to"/> (exclusive).</summary>
    public bool Overlaps(DateTime from, DateTime to) => Start < to && End > from;

    /// <summary>Gets the minutes this period shares with <paramref name="from"/> to <paramref name="to"/>.</summary>
    public int OverlapMinutes(DateTime from, DateTime to)
    {
        var start = Start > from ? Start : from;
        var end = End < to ? End : to;
        return end > start ? (int)(end - start).TotalMinutes : 0;
    }
}
