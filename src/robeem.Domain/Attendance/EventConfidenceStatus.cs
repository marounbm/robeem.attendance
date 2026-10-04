namespace robeem.DataModel
{
    /// <summary>
    /// How the normalized event type was obtained (CLAUDE.md §1.6). Stored as its name.
    /// </summary>
    public enum EventConfidenceStatus
    {
        /// <summary>The vendor labeled the punch.</summary>
        Certain,

        /// <summary>robeem interpreted an unlabeled punch through the policy's interpretation mode.</summary>
        Inferred,
        Ambiguous,
    }
}
