namespace robeem.DataModel
{
    /// <summary>
    /// Who must approve an attendance correction. Stored as its name.
    /// </summary>
    public enum CorrectionApprovalWorkflow
    {
        Manager,
        HR,

        /// <summary>The manager, then HR.</summary>
        Both,
    }
}
