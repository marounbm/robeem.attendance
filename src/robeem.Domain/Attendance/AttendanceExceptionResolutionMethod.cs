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

        /// <summary>A manager or HR accepted it as it is, with a reason. Only for a break exception: there is no punch to supply.</summary>
        Accepted,
    }
}
