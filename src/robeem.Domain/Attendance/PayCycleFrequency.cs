namespace robeem.DataModel
{
    /// <summary>How often an employee is paid, which makes their pay period. Stored as its name.</summary>
    public enum PayCycleFrequency
    {
        /// <summary>Seven days, starting on the anchor date's weekday.</summary>
        Weekly,

        /// <summary>Fourteen days, starting on the anchor date.</summary>
        EveryTwoWeeks,

        /// <summary>From the start day of one month to the day before it in the next (the 26th to the 25th).</summary>
        Monthly,
    }
}
