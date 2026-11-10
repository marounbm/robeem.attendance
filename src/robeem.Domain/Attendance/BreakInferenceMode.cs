namespace robeem.DataModel
{
    /// <summary>
    /// Whether a break is inferred for a device that sends no break type (CLAUDE.md §1.6). An inferred break is always marked as
    /// inferred, and a punch is never invented. Stored as its name.
    /// </summary>
    public enum BreakInferenceMode
    {
        /// <summary>A shift's punches are used as the device typed them.</summary>
        Off,

        /// <summary>
        /// On a shift with a scheduled break, a check-out followed by a check-in near the scheduled break is read as the break, when
        /// exactly one such gap exists.
        /// </summary>
        FromGap,
    }
}
