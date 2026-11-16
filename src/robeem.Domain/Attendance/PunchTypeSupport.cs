namespace robeem.DataModel
{
    /// <summary>
    /// What an attendance machine puts on each punch it records. It decides how the punches of the machine are read: only a machine that sends no
    /// break types has a gap between a check-out and a check-in read as a break, and only a machine that sends no types at all has its punches typed
    /// in order. Stored as its name; <see cref="InOut"/> is first so that it is the default.
    /// </summary>
    public enum PunchTypeSupport
    {
        /// <summary>Check-in and check-out only. A check-out and the next check-in may be the break (a rule can read them so).</summary>
        InOut,

        /// <summary>Check-in, check-out, break-out and break-in. A check-out and a check-in are a real exit: they are never read as a break.</summary>
        InOutAndBreak,

        /// <summary>No types: every punch is the same. They are typed in order (check-in, check-out, ...), marked as inferred.</summary>
        None,
    }
}
