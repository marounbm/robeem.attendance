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
