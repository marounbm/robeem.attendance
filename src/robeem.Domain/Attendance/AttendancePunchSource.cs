namespace robeem.DataModel
{
    /// <summary>
    /// Where an <see cref="AttendanceRecord"/>'s actual punch came from. Stored as its name.
    /// </summary>
    public enum AttendancePunchSource
    {
        /// <summary>A normalized punch whose type the vendor gave.</summary>
        Normalized,

        /// <summary>An unlabeled punch whose type the policy's interpretation mode assigned (CLAUDE.md §1.6).</summary>
        Inferred,

        /// <summary>An approved correction (P9).</summary>
        Corrected,
    }
}
