namespace robeem.DataModel
{
    /// <summary>
    /// What an <see cref="AttendanceProcessingBatch"/> run was. Stored as its name.
    /// </summary>
    public enum AttendanceBatchType
    {
        InitialSync,
        IncrementalSync,
        Reprocess,
        ManualUpload,
        Reconciliation,
        Webhook,
    }
}
