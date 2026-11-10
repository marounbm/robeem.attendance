namespace robeem.DataModel
{
    /// <summary>How an employee is paid. Stored as its name.</summary>
    public enum PayBasis
    {
        /// <summary>Paid by the schedule: the days are compared with the shifts they are expected to work.</summary>
        Scheduled,

        /// <summary>
        /// Paid by the hours worked (wages). Never compared with a schedule, so a missing day is not an exception: an exception is raised
        /// only when the hours worked cannot be calculated.
        /// </summary>
        Hourly,
    }
}
