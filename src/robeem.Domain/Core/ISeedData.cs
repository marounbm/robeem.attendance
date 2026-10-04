namespace robeem.DataModel
{
    /// <summary>
    /// Marker for reference/configuration entities that may be populated by a seed contributor.
    /// The seed runner refuses to insert any entity type that does not carry this marker.
    /// </summary>
    public interface ISeedData
    {
    }
}
