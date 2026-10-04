namespace robeem.DataModel
{
    /// <summary>
    /// Where an <see cref="AttendanceCorrection"/> stands (study §11.1). Stored as its name.
    /// </summary>
    public enum AttendanceCorrectionStatus
    {
        /// <summary>Proposed, and waiting for the decisions its approval workflow requires.</summary>
        Pending,

        /// <summary>Every required approval is in; the correction is about to be applied.</summary>
        Approved,
        Rejected,

        /// <summary>Approved, and folded into the attendance calculation.</summary>
        Applied,
    }
}
