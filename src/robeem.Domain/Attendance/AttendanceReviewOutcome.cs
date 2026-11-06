namespace robeem.DataModel
{
    /// <summary>
    /// What a reviewer decided about an <see cref="AttendanceRecord"/> that was waiting for review: the outcome of a review
    /// <see cref="AttendanceApproval"/>.
    /// </summary>
    public enum AttendanceReviewOutcome
    {
        Approved,
        Rejected,
    }
}
