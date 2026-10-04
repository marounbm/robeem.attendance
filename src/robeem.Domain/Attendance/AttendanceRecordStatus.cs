namespace robeem.DataModel
{
    /// <summary>
    /// Review state of an <see cref="AttendanceRecord"/> or <see cref="AttendanceDaySummary"/>.
    /// Stored as its name.
    /// </summary>
    public enum AttendanceRecordStatus
    {
        /// <summary>Calculated, with nothing to review (a day summary without records).</summary>
        Calculated,

        /// <summary>A person must look at it: incomplete punches, unscheduled work, or overtime needing pre-approval.</summary>
        PendingReview,
        Approved,
        Rejected,
    }
}
