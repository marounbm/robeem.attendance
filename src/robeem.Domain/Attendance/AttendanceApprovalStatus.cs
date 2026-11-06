namespace robeem.DataModel
{
    /// <summary>Where an <see cref="AttendanceApproval"/> stands. Stored as its name.</summary>
    public enum AttendanceApprovalStatus
    {
        /// <summary>Waiting for the decisions its workflow requires.</summary>
        Pending,

        /// <summary>A review was approved.</summary>
        Approved,

        /// <summary>Someone rejected it.</summary>
        Rejected,

        /// <summary>A correction was approved by every step it needs, and folded into the attendance calculation.</summary>
        Applied,

        /// <summary>
        /// A review that nobody needs to decide any more: the record was recalculated into a new version, or the engine found nothing
        /// left to review. Not a human decision.
        /// </summary>
        Superseded,
    }
}
