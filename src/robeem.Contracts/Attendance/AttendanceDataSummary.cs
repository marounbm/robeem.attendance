namespace robeem.Contracts.Attendance;

/// <summary>What deleting all attendance data would delete, and whether it is allowed here.</summary>
public sealed class AttendanceDataSummary
{
    /// <summary>Gets or sets the tenant whose data it is; the administrator types it to confirm.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the host allows the deletion (<c>Attendance:DataReset:Enabled</c>).</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets what would be deleted, with how many rows of each.</summary>
    public List<AttendanceDataCount> Deletes { get; set; } = [];

    /// <summary>Gets or sets what is kept, with how many rows of each.</summary>
    public List<AttendanceDataCount> Keeps { get; set; } = [];
}
