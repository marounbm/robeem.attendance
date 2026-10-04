namespace robeem.DataModel
{
    /// <summary>
    /// Where an <see cref="AttendanceException"/> stands (study §11.1). Stored as its name.
    /// </summary>
    public enum AttendanceExceptionStatus
    {
        /// <summary>Raised, and waiting for a person.</summary>
        Open,

        /// <summary>A correction has been proposed and is waiting for its decision.</summary>
        PendingCorrection,
        Closed,
    }
}
