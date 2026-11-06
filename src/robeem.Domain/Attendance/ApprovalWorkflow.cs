namespace robeem.DataModel
{
    /// <summary>Who must decide an <see cref="AttendanceApproval"/>, and in which order. Stored as its name.</summary>
    public enum ApprovalWorkflow
    {
        /// <summary>The employee's manager alone.</summary>
        Manager,

        /// <summary>HR alone.</summary>
        HR,

        /// <summary>The manager, then HR.</summary>
        Both,

        /// <summary>One decision, by the employee's manager or by HR (a review).</summary>
        ManagerOrHr,
    }
}
