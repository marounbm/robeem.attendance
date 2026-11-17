namespace robeem.DataModel
{
    /// <summary>
    /// What kind of problem an <see cref="AttendanceException"/> reports (study §9). Stored as its name.
    /// </summary>
    public enum AttendanceExceptionType
    {
        /// <summary>An expected punch is absent; <see cref="AttendanceException.MissingEventType"/> says which.</summary>
        MissingPunch,

        /// <summary>The punches are in an order the attendance rules don't allow (study §14.1).</summary>
        InvalidSequence,

        /// <summary>The breaks lasted longer than the scheduled break allows. A policy decides whether this is raised.</summary>
        LongBreak,

        /// <summary>The break started later than scheduled. A policy decides whether this is raised.</summary>
        LateBreak,

        /// <summary>The break started earlier than scheduled. A policy decides whether this is raised.</summary>
        EarlyBreak,

        /// <summary>
        /// A scheduled shift passed without a single punch, on a day with no approved leave or holiday. It is settled by the punches
        /// arriving or being corrected, by a manager or HR accepting it with a reason, or by a leave or holiday that is entered later.
        /// </summary>
        NoShow,

        /// <summary>
        /// A complete day, after the shift is over, in which the employee worked less than the shift schedules (late, left early, a longer
        /// break, or any mix). There is no punch to supply: a manager or HR accepts it with a reason, or the day's punches are corrected. The
        /// rules decide how much less is a problem.
        /// </summary>
        ShortHours,
    }
}
