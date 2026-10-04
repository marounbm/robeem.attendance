namespace robeem.DataModel
{
    /// <summary>
    /// How far normalization got with a <see cref="RawAttendanceTransaction"/>. Stored as its name.
    /// </summary>
    public enum RawTransactionProcessingStatus
    {
        Pending,
        Normalized,

        /// <summary>The raw row is structurally unusable; see <see cref="RawAttendanceTransaction.ProcessingError"/>. No normalized row exists for it.</summary>
        Failed,
        Skipped,
    }
}
