namespace robeem.Application.Attendance.TestEmployees;

/// <summary>
/// Whether an administrator may load test employees from a file. Off unless a host turns it on (the Development settings do): they are people
/// who do not exist, so a production host has to choose it.
/// </summary>
public sealed class TestEmployeeOptions
{
    public const string SectionName = "Attendance:TestEmployees";

    public bool Enabled { get; set; }
}
