namespace robeem.DataModel
{
    /// <summary>
    /// Whether the processing engine has used a normalized punch. Stored as its name.
    /// </summary>
    public enum NormalizedTransactionProcessingStatus
    {
        Pending,
        Processed,
        Excluded,
    }
}
