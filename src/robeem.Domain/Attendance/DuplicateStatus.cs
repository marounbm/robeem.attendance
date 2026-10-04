namespace robeem.DataModel
{
    /// <summary>
    /// Duplicate decision taken at normalization (CLAUDE.md §1.4); processing trusts it. Stored as its name.
    /// </summary>
    public enum DuplicateStatus
    {
        /// <summary>The canonical row for its duplicate key.</summary>
        Unique,

        /// <summary>Same duplicate key as an existing Unique row; see <see cref="NormalizedAttendanceTransaction.DuplicateOfTransactionId"/>.</summary>
        DuplicateConfirmed,
        DuplicateCandidate,
    }
}
