namespace robeem.DataModel
{
    /// <summary>
    /// Vendor-neutral type of a punch (study §7.3). Stored as its name.
    /// </summary>
    public enum NormalizedEventType
    {
        CheckIn,
        CheckOut,
        BreakOut,
        BreakIn,

        /// <summary>The punch has no usable type and none was inferred.</summary>
        Unknown,
        Other,
    }
}
