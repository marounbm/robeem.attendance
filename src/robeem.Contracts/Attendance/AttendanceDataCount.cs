namespace robeem.Contracts.Attendance;

/// <summary>A kind of data and how many rows of it there are.</summary>
public sealed class AttendanceDataCount
{
    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }
}
