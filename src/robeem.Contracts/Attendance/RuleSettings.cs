namespace robeem.Contracts.Attendance;

/// <summary>
/// The standard rules of a scope as plain settings: a switch and a number for each thing HR usually changes. It is a way to read and write the
/// same rules the Rules grid shows, not a second copy of them. Two settings are equal when every value is.
/// </summary>
public sealed record RuleSettings
{
    /// <summary>Gets or sets a value indicating whether breaks that run past the scheduled break raise an exception.</summary>
    public bool LongBreakRaises { get; set; }

    /// <summary>Gets or sets how many minutes the breaks together may run past the scheduled break before they are long.</summary>
    public int LongBreakMinutesOver { get; set; }

    public bool LateBreakRaises { get; set; }

    /// <summary>Gets or sets how many minutes after its scheduled start a break may begin before it is late.</summary>
    public int LateBreakMinutesOver { get; set; }

    public bool EarlyBreakRaises { get; set; }

    /// <summary>Gets or sets how many minutes before its scheduled start a break may begin before it is early.</summary>
    public int EarlyBreakMinutesOver { get; set; }

    /// <summary>Gets or sets a value indicating whether a shift with a scheduled break, worked with no break punches, raises an exception.</summary>
    public bool NoBreakRaises { get; set; }

    public bool NoShowRaises { get; set; }

    /// <summary>Gets or sets how many minutes after a shift ends a shift with no punch becomes a no-show.</summary>
    public int NoShowWaitMinutes { get; set; }

    /// <summary>Gets or sets how many minutes after a shift ends a missing check-out becomes an exception. It is always raised in the end.</summary>
    public int MissingCheckOutWaitMinutes { get; set; }

    /// <summary>Gets or sets a value indicating whether a short break (a toilet visit) counts as work.</summary>
    public bool ShortBreakIsWork { get; set; }

    /// <summary>Gets or sets the length below which a break is short, in minutes.</summary>
    public int ShortBreakMinutes { get; set; }

    /// <summary>Gets or sets a value indicating whether a gap between a check-out and the next check-in is read as the break (for devices that send no break type).</summary>
    public bool GapIsBreak { get; set; }

    /// <summary>Gets or sets how far from the scheduled break start a gap may start and still be read as the break, in minutes.</summary>
    public int GapWindowMinutes { get; set; }

    /// <summary>Gets or sets the longest gap that is read as a break, in minutes.</summary>
    public int GapMaxMinutes { get; set; }
}
