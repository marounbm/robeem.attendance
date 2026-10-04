namespace robeem.DataModel
{
    /// <summary>
    /// What kind of problem an <see cref="AttendanceException"/> reports (study §9). Stored as its name.
    /// </summary>
    public enum AttendanceExceptionType
    {
        /// <summary>An expected punch is absent; <see cref="AttendanceException.MissingEventType"/> says which.</summary>
        MissingPunch,

        /// <summary>The punches are in an order the attendance rules don't allow (study §14.1).</summary>
        InvalidSequence,
    }
}
