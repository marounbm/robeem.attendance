namespace robeem.DataModel
{
    /// <summary>
    /// How a closed <see cref="AttendanceException"/> was resolved. Stored as its name.
    /// </summary>
    public enum AttendanceExceptionResolutionMethod
    {
        /// <summary>Reprocessing found the problem gone, for instance because the missing punch arrived late.</summary>
        AutoResolved,

        /// <summary>An approved correction fixed it.</summary>
        Correction,
    }
}
