namespace robeem.Application.Attendance;

/// <summary>How a reset of a tenant's attendance data ended.</summary>
public enum AttendanceDataResetOutcome
{
    Done,

    /// <summary>The host has not turned the deletion on.</summary>
    Disabled,

    /// <summary>What was typed is not the tenant id.</summary>
    ConfirmationMismatch,
}
