namespace robeem.DataModel
{
    /// <summary>
    /// Data-quality flag raised on a normalized punch. Stored as its name.
    /// </summary>
    public enum TransactionExceptionStatus
    {
        None,
        FlaggedInvalidSequence,
        FlaggedUnrecognizedEmployee,
        FlaggedUnrecognizedDevice,
    }
}
