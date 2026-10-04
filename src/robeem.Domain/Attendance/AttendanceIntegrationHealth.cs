namespace robeem.DataModel
{
    /// <summary>
    /// Observed health of an <see cref="AttendanceIntegration"/>, maintained by the sync and monitoring jobs (P10, P13). Stored as its name.
    /// </summary>
    public enum AttendanceIntegrationHealth
    {
        /// <summary>No synchronization has run yet.</summary>
        Unknown,
        Healthy,
        Degraded,
        Unhealthy,
    }
}
