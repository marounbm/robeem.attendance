namespace robeem.DataModel
{
    /// <summary>What an <see cref="AttendanceApproval"/> is asked to decide. Stored as its name.</summary>
    public enum AttendanceApprovalType
    {
        /// <summary>The edits of an <see cref="AttendanceCorrection"/> request, proposed by the employee.</summary>
        Correction,

        /// <summary>An <see cref="AttendanceRecord"/> the engine left <c>PendingReview</c>: overtime, unscheduled work or an incomplete day.</summary>
        Review,
    }
}
