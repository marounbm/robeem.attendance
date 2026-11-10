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

        /// <summary>A manager or HR accepted it as it is, with a reason. For a break exception or a no-show: there is no punch to supply.</summary>
        Accepted,

        /// <summary>A leave or holiday entered in the system covers the day, so nothing was expected. The note says which.</summary>
        ExcusedAbsence,
    }
}
