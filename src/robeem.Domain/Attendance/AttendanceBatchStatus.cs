namespace robeem.DataModel
{
    /// <summary>
    /// Outcome of an <see cref="AttendanceProcessingBatch"/>. Stored as its name.
    /// </summary>
    public enum AttendanceBatchStatus
    {
        Running,
        Succeeded,
        Failed,

        /// <summary>The batch completed, but some records failed (see <see cref="AttendanceProcessingBatch.RecordsFailed"/>).</summary>
        PartiallyFailed,
    }
}
