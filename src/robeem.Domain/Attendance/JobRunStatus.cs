namespace robeem.DataModel
{
    /// <summary>How a background job run stands or ended. Stored as its name.</summary>
    public enum JobRunStatus
    {
        Running,
        Succeeded,

        /// <summary>The run threw; the reason is in <see cref="AttendanceJobRun.Summary"/>.</summary>
        Failed,

        /// <summary>A scheduled time passed with no run, because the Api was not running.</summary>
        Missed,

        /// <summary>The Api shut down while the run was in progress.</summary>
        Cancelled,
    }
}
