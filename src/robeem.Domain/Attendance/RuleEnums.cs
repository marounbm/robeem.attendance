namespace robeem.DataModel
{
    /// <summary>Where an attendance rule set stands. A published set is never edited; a change is a new draft, then a new published set. Stored as its name.</summary>
    public enum RuleSetStatus
    {
        Draft,
        Published,
        Retired,
    }

    /// <summary>When in the calculation a rule is looked at. Stored as its name.</summary>
    public enum RuleStage
    {
        /// <summary>While the punches are read: whether a gap between a check-out and a check-in is the break.</summary>
        Interpretation,

        /// <summary>After the day is calculated: whether a finding becomes an exception.</summary>
        Exception,
    }

    /// <summary>What a rule does when its conditions hold. Stored as its name.</summary>
    public enum RuleAction
    {
        /// <summary>Read the gap as the break: its two punches are retyped and marked inferred. A punch is never invented.</summary>
        InferBreak,

        /// <summary>Raise an exception for the finding.</summary>
        RaiseException,

        /// <summary>Do not raise an exception for the finding. Needs a reason, and is audited when the rule set is published.</summary>
        SuppressException,

        /// <summary>Count a short break (a break-out followed at once by its break-in) as work: both punches are left out of the calculation. A stored punch is never changed.</summary>
        IgnoreShortBreak,

        /// <summary>
        /// A day with a check-out near the start of the shift and no check-in at all: the check-out is read as the check-in (retyped and marked
        /// inferred). The punch exists; none is invented.
        /// </summary>
        LoneCheckOutIsCheckIn,

        /// <summary>A day with a check-in near the end of the shift and no check-out at all: the check-in is read as the check-out (retyped and marked inferred).</summary>
        LoneCheckInIsCheckOut,

        /// <summary>
        /// The first two punches of the day are a check-in followed by another check-in or a check-out, close together and near the start of the shift: the
        /// first is the check-in and the second is left out of the calculation. A stored punch is never changed.
        /// </summary>
        KeepFirstMorningPunch,

        /// <summary>
        /// The last two punches of the day are both check-outs, close together and near the end of the shift: the first is the check-out and the second is left
        /// out of the calculation. A check-in followed by a check-out is <see cref="KeepFirstEveningCheckInPunch"/>.
        /// </summary>
        KeepFirstEveningPunch,

        /// <summary>
        /// A break-in where the break-out belongs (no break is open, and the punch is near the scheduled start of the break): the break-in is read as the
        /// break-out (retyped and marked inferred). It covers a break-in said twice and a break-in instead of a break-out. The punch exists; none is invented.
        /// </summary>
        BreakInIsBreakOut,

        /// <summary>
        /// A break-out where the break-in belongs (a break is already open, and the punch is near the scheduled end of the break): the break-out is read as the
        /// break-in (retyped and marked inferred). It covers a break-out said twice and a break-out instead of a break-in.
        /// </summary>
        BreakOutIsBreakIn,

        /// <summary>
        /// The last two punches of the day are both check-outs, close together, and the second is near the end of the shift: the second is the check-out and
        /// the first is left out of the calculation (the person punched out, then again, and the later one is the one that counts). A stored punch is never
        /// changed. The mirror of <see cref="KeepFirstEveningPunch"/>, which measures from the first; when both are on, that one reads the pair first.
        /// </summary>
        KeepLastEveningPunch,

        /// <summary>
        /// The last two punches of the day are a check-in (after a check-in, with no check-out between) and then a check-out, close together and near the end
        /// of the shift: the check-in is read as the check-out (retyped and marked inferred) and the check-out is left out of the calculation. It is the second
        /// half of what <see cref="KeepFirstEveningPunch"/> used to do, which now reads two check-outs only.
        /// </summary>
        KeepFirstEveningCheckInPunch,

        /// <summary>
        /// The first two punches of the day are both check-ins, close together, and the second is near the start of the shift: the second is the check-in and the
        /// first is left out of the calculation. The mirror of <see cref="KeepFirstMorningPunch"/>, which keeps the first; when both are on, that one reads the pair first.
        /// </summary>
        KeepLastMorningPunch,

        /// <summary>
        /// The last two punches of the day are a check-in (after a check-in, with no check-out between) and then a check-out, close together, and the check-out is
        /// near the end of the shift: the check-in is left out of the calculation and the check-out counts as it is. Nothing is retyped or inferred, because the
        /// kept punch is one the machine labelled a check-out. The other way to read this pair is <see cref="KeepFirstEveningCheckInPunch"/>; when both are on,
        /// that one reads the pair first.
        /// </summary>
        KeepEveningCheckOutPunch,

        /// <summary>
        /// The first two punches of the day are a check-out and then a check-in, close together, and the check-in is near the start of the shift: the check-out is
        /// left out of the calculation and the check-in counts as it is (a check-out with no check-in before it is a slip). Nothing is retyped or inferred.
        /// </summary>
        KeepMorningCheckInPunch,

        /// <summary>
        /// Two check-ins one after the other, no more than so many minutes apart, anywhere in the day (no shift needed): the first counts and the later one is
        /// left out of the calculation. The edge rules read a pair at the start or end of the shift first. Repeats are read until none is left, so three taps
        /// become one.
        /// </summary>
        KeepFirstRepeatedCheckIn,

        /// <summary>Two check-ins one after the other, close together, anywhere in the day: the later one counts and the earlier one is left out.</summary>
        KeepLastRepeatedCheckIn,

        /// <summary>Two check-outs one after the other, close together, anywhere in the day: the first counts and the later one is left out.</summary>
        KeepFirstRepeatedCheckOut,

        /// <summary>Two check-outs one after the other, close together, anywhere in the day: the later one counts, which is the time the person left, and the earlier one is left out.</summary>
        KeepLastRepeatedCheckOut,

        /// <summary>Two break-outs, or two break-ins, one after the other and close together, anywhere in the day: the first counts and the later one is left out.</summary>
        KeepFirstRepeatedBreakPunch,
    }

    /// <summary>What an exception rule is about. Stored as its name.</summary>
    public enum RuleFinding
    {
        /// <summary>The breaks lasted longer than the scheduled break allows.</summary>
        LongBreak,

        /// <summary>The break started later than scheduled.</summary>
        LateBreak,

        /// <summary>The break started earlier than scheduled.</summary>
        EarlyBreak,

        /// <summary>A shift with a scheduled break was worked with no break punches: the break punches are reported missing.</summary>
        NoBreak,

        /// <summary>A scheduled shift passed without a single punch.</summary>
        NoShow,

        /// <summary>
        /// A shift is over and the employee is still checked in (or a break is still open). The rules say how long to wait after the shift ends before it
        /// is raised, but never whether: it is always raised 24 hours after the shift ended, so a day can always be corrected.
        /// </summary>
        MissingCheckOut,

        /// <summary>
        /// A complete shift, once it is over, in which less was worked than scheduled (late, left early, a longer break, or any mix). The fact
        /// <see cref="RuleFact.MinutesOver"/> is how many minutes short the day is, after the time on approved leave.
        /// </summary>
        ShortHours,

        /// <summary>
        /// The check-in came after the shift started, beyond the policy's grace and any approved leave at the start. The fact
        /// <see cref="RuleFact.MinutesOver"/> is how many minutes late, so a rule can allow some more.
        /// </summary>
        LateCheckIn,

        /// <summary>
        /// The check-out came before the shift ended, beyond the policy's grace and any approved leave at the end. The fact
        /// <see cref="RuleFact.MinutesOver"/> is how many minutes early.
        /// </summary>
        EarlyCheckOut,

        /// <summary>
        /// A complete shift worked for longer than scheduled, by at least the policy's overtime threshold. The fact <see cref="RuleFact.MinutesOver"/>
        /// is how many minutes of overtime, so a rule can allow some more. It is in addition to the policy's pre-approval of overtime, which is unchanged.
        /// </summary>
        Overtime,

        /// <summary>
        /// Punches on a day, or at a time, when no shift is scheduled for an employee who is not paid by the hour (a day off, a holiday, or no schedule at
        /// all). It is in addition to the review the record waits for, which is unchanged.
        /// </summary>
        UnscheduledWork,
    }

    /// <summary>
    /// A fact a rule's condition can look at. The list is fixed: every fact is something the calculation already knows, so a rule can
    /// be shown as a sentence and checked before it is published.
    /// </summary>
    public enum RuleFact
    {
        /// <summary>How long the gap between a check-out and the next check-in lasted, in minutes. Interpretation only.</summary>
        GapMinutes,

        /// <summary>How far the gap's start is from the scheduled break start, in minutes, either way. Interpretation only.</summary>
        GapStartOffsetMinutes,

        /// <summary>How much of the gap approved part-day leave covers, in percent. Interpretation only.</summary>
        GapExcusedPercent,

        /// <summary>
        /// How far the finding goes past what is allowed, in minutes: the break beyond the scheduled break (a long break), or how early or
        /// late the break started. Exception only.
        /// </summary>
        MinutesOver,

        /// <summary>Whether the shift has a scheduled break (1 or 0).</summary>
        ShiftHasBreak,

        /// <summary>Whether the shift ends after midnight (1 or 0).</summary>
        ShiftIsOvernight,

        /// <summary>The minutes of the shift covered by part-day approved leave. Exception only.</summary>
        ExcusedMinutes,

        /// <summary>How long ago the shift ended, in minutes. Only a rule about a no-show or a missing check-out can use it: it is how long to wait before a shift is one of those.</summary>
        MinutesAfterShiftEnd,

        /// <summary>How long a break lasted, in whole minutes (a gap between a check-out and a check-in, or a break-out and its break-in). Only a rule that counts a short break as work can use it.</summary>
        BreakMinutes,

        /// <summary>How far the punch a rule looks at is from the shift's start (morning rules) or end (evening rules), in minutes, either way. Only the rules that fix punches can use it.</summary>
        PunchOffsetMinutes,

        /// <summary>How many minutes apart the two punches a rule looks at are. Only a rule that keeps the first of two close punches can use it.</summary>
        PunchGapMinutes,
    }

    /// <summary>How a condition compares a fact with its value. Stored as its name.</summary>
    public enum RuleOperator
    {
        Equals,
        NotEquals,
        LessThan,
        LessOrEqual,
        GreaterThan,
        GreaterOrEqual,
    }
}
