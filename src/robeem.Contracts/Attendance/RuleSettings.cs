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

    /// <summary>Gets or sets a value indicating whether a complete day worked for less time than the shift schedules raises an exception.</summary>
    public bool ShortHoursRaises { get; set; }

    /// <summary>Gets or sets how many minutes short of the scheduled time a day may be before it raises an exception (after the time on approved leave).</summary>
    public int ShortHoursMinutesOver { get; set; }

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

    /// <summary>Gets or sets a value indicating whether a day with a check-out near the start of the shift and no check-in has that check-out read as the check-in.</summary>
    public bool LoneCheckOutIsCheckIn { get; set; }

    /// <summary>Gets or sets how far from the start of the shift a lone check-out may be and still be read as the check-in, in minutes.</summary>
    public int LoneCheckOutWindowMinutes { get; set; }

    /// <summary>Gets or sets a value indicating whether a day with a check-in near the end of the shift and no check-out has that check-in read as the check-out.</summary>
    public bool LoneCheckInIsCheckOut { get; set; }

    /// <summary>Gets or sets how far from the end of the shift a lone check-in may be and still be read as the check-out, in minutes.</summary>
    public int LoneCheckInWindowMinutes { get; set; }

    /// <summary>Gets or sets a value indicating whether two punches close together at the start of the day count as one: the first is the check-in, the second is left out.</summary>
    public bool MorningDuplicateIsOne { get; set; }

    /// <summary>Gets or sets how far from the start of the shift the first of two close punches may be, in minutes.</summary>
    public int MorningDuplicateWindowMinutes { get; set; }

    /// <summary>Gets or sets how many minutes apart two punches at the start of the day may be and still count as one.</summary>
    public int MorningDuplicateGapMinutes { get; set; }

    /// <summary>Gets or sets a value indicating whether two punches close together at the end of the day count as one: the first is the check-out, the second is left out.</summary>
    public bool EveningDuplicateIsOne { get; set; }

    /// <summary>Gets or sets how far from the end of the shift the first of two close punches may be, in minutes.</summary>
    public int EveningDuplicateWindowMinutes { get; set; }

    /// <summary>Gets or sets how many minutes apart two punches at the end of the day may be and still count as one.</summary>
    public int EveningDuplicateGapMinutes { get; set; }
}
