namespace robeem.DataModel
{
    /// <summary>
    /// How a punch without a vendor event type is interpreted (CLAUDE.md §1.6). An interpreted type is always marked as inferred; a missing punch is never invented. Stored as its name.
    /// </summary>
    public enum UnlabeledPunchInterpretationMode
    {
        /// <summary>Unlabeled punches are not interpreted; they raise an exception.</summary>
        Off,

        /// <summary>Alternate Check-In / Check-Out in time order within the shift.</summary>
        Sequential,

        /// <summary>Interpret through configurable rules.</summary>
        RulesEngine,
    }
}
