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

        /// <summary>
        /// Twice a month: from the start day to 14 days later, and from the day after that to the day before the start day of the next month. With
        /// start day 1 that is the 1st to the 15th and the 16th to the end of the month.
        /// </summary>
        SemiMonthly,

        /// <summary>Twenty-eight days (thirteen pay periods a year), starting on the anchor date.</summary>
        EveryFourWeeks,
    }
}
