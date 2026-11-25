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

    /// <summary>Gets or sets a value indicating whether a check-in later than the shift start (beyond the policy's grace) raises an exception.</summary>
    public bool LateCheckInRaises { get; set; }

    /// <summary>Gets or sets how many minutes late, beyond the grace, a check-in may be before it raises an exception.</summary>
    public int LateCheckInMinutesOver { get; set; }

    /// <summary>Gets or sets a value indicating whether a check-out earlier than the shift end (beyond the policy's grace) raises an exception.</summary>
    public bool EarlyCheckOutRaises { get; set; }

    /// <summary>Gets or sets how many minutes early, beyond the grace, a check-out may be before it raises an exception.</summary>
    public int EarlyCheckOutMinutesOver { get; set; }

    /// <summary>Gets or sets a value indicating whether a complete shift worked longer than scheduled raises an exception.</summary>
    public bool OvertimeRaises { get; set; }

    /// <summary>Gets or sets how many minutes of overtime a day may have before it raises an exception (on top of the policy's overtime threshold).</summary>
    public int OvertimeMinutesOver { get; set; }

    /// <summary>Gets or sets a value indicating whether punches with no scheduled shift (a day off or a holiday) raise an exception.</summary>
    public bool UnscheduledWorkRaises { get; set; }

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

    /// <summary>Gets or sets a value indicating whether two check-outs close together at the end of the day count as one: the first is the check-out, the second is left out.</summary>
    public bool EveningDuplicateIsOne { get; set; }

    /// <summary>Gets or sets how far from the end of the shift the first of two close punches may be, in minutes.</summary>
    public int EveningDuplicateWindowMinutes { get; set; }

    /// <summary>Gets or sets how many minutes apart two punches at the end of the day may be and still count as one.</summary>
    public int EveningDuplicateGapMinutes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether two check-outs close together at the end of the day count as one, the last one: the second is the check-out and the
    /// first is left out. It can't be on together with <see cref="EveningDuplicateIsOne"/>, which keeps the first.
    /// </summary>
    public bool EveningLastPunchCounts { get; set; }

    /// <summary>Gets or sets how far from the end of the shift the second of two close check-outs, the one that is kept, may be, in minutes.</summary>
    public int EveningLastPunchWindowMinutes { get; set; }

    /// <summary>Gets or sets how many minutes apart two check-outs at the end of the day may be and still count as one.</summary>
    public int EveningLastPunchGapMinutes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a check-in followed by a check-out close together at the end of the day count as one check-out: the check-in is read
    /// as the check-out and the second punch is left out. The check-in is one that follows a check-in, with no check-out between.
    /// </summary>
    public bool EveningInThenOutIsOne { get; set; }

    /// <summary>Gets or sets how far from the end of the shift the check-in of such a pair may be, in minutes.</summary>
    public int EveningInThenOutWindowMinutes { get; set; }

    /// <summary>Gets or sets how many minutes apart the check-in and the check-out at the end of the day may be and still count as one.</summary>
    public int EveningInThenOutGapMinutes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether two check-ins close together at the start of the day count as one, the last one: the second is the check-in and the
    /// first is left out. It can't be on together with <see cref="MorningDuplicateIsOne"/>, which keeps the first.
    /// </summary>
    public bool MorningLastPunchCounts { get; set; }

    /// <summary>Gets or sets how far from the start of the shift the second of two close check-ins, the one that is kept, may be, in minutes.</summary>
    public int MorningLastPunchWindowMinutes { get; set; }

    /// <summary>Gets or sets how many minutes apart two check-ins at the start of the day may be and still count as one.</summary>
    public int MorningLastPunchGapMinutes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether, of a check-in (after a check-in) then a check-out close together at the end of the day, the check-out counts as it is and
    /// the check-in is left out. It can't be on together with <see cref="EveningInThenOutIsOne"/>, which reads the check-in as the check-out instead.
    /// </summary>
    public bool EveningInThenOutLast { get; set; }

    /// <summary>Gets or sets how far from the end of the shift the check-out of such a pair, the one that is kept, may be, in minutes.</summary>
    public int EveningInThenOutLastWindowMinutes { get; set; }

    /// <summary>Gets or sets how many minutes apart the check-in and the check-out at the end of the day may be and still be read this way.</summary>
    public int EveningInThenOutLastGapMinutes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether, of a check-out then a check-in close together at the start of the day, the check-in counts as it is and the check-out,
    /// which comes before any check-in, is left out.
    /// </summary>
    public bool MorningOutThenInCounts { get; set; }

    /// <summary>Gets or sets how far from the start of the shift the check-in of such a pair, the one that is kept, may be, in minutes.</summary>
    public int MorningOutThenInWindowMinutes { get; set; }

    /// <summary>Gets or sets how many minutes apart the check-out and the check-in at the start of the day may be and still be read this way.</summary>
    public int MorningOutThenInGapMinutes { get; set; }

    /// <summary>Gets or sets a value indicating whether, of two close check-ins one after the other anywhere in the day, the first counts and the later one is left out.</summary>
    public bool RepeatedCheckInFirst { get; set; }

    /// <summary>Gets or sets a value indicating whether, of two close check-ins one after the other anywhere in the day, the later one counts. Not together with <see cref="RepeatedCheckInFirst"/>.</summary>
    public bool RepeatedCheckInLast { get; set; }

    /// <summary>Gets or sets a value indicating whether, of two close check-outs one after the other anywhere in the day, the first counts and the later one is left out.</summary>
    public bool RepeatedCheckOutFirst { get; set; }

    /// <summary>Gets or sets a value indicating whether, of two close check-outs one after the other anywhere in the day, the later one counts. Not together with <see cref="RepeatedCheckOutFirst"/>.</summary>
    public bool RepeatedCheckOutLast { get; set; }

    /// <summary>Gets or sets a value indicating whether, of two close break-outs or two close break-ins, the first counts and the later one is left out.</summary>
    public bool RepeatedBreakPunchFirst { get; set; }

    /// <summary>Gets or sets how many minutes apart two punches of the same kind may be and still be one punch said twice; one number for all five rules of repeated punches.</summary>
    public int RepeatedGapMinutes { get; set; }

    /// <summary>Gets or sets a value indicating whether a break-in near the scheduled start of the break, where no break is open, is read as the break-out.</summary>
    public bool BreakInIsBreakOut { get; set; }

    /// <summary>Gets or sets how far from the scheduled start of the break a break-in may be and still be read as the break-out, in minutes.</summary>
    public int BreakInIsBreakOutWindowMinutes { get; set; }

    /// <summary>Gets or sets a value indicating whether a break-out near the scheduled end of the break, where a break is already open, is read as the break-in.</summary>
    public bool BreakOutIsBreakIn { get; set; }

    /// <summary>Gets or sets how far from the scheduled end of the break a break-out may be and still be read as the break-in, in minutes.</summary>
    public int BreakOutIsBreakInWindowMinutes { get; set; }
}
