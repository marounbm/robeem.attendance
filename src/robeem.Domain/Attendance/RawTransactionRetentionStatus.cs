namespace robeem.DataModel
{
    /// <summary>
    /// Retention state of a <see cref="RawAttendanceTransaction"/>. Stored as its name.
    /// </summary>
    public enum RawTransactionRetentionStatus
    {
        Active,
        Archived,
        PendingDeletion,
    }
}
