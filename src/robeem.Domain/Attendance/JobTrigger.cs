namespace robeem.DataModel
{
    /// <summary>What started a background job run. Stored as its name.</summary>
    public enum JobTrigger
    {
        Scheduled,

        /// <summary>Somebody pressed Run now.</summary>
        Manual,
    }
}
