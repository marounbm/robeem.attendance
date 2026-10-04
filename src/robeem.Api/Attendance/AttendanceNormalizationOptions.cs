namespace robeem.Api.Attendance;

public sealed class AttendanceNormalizationOptions
{
    public const string SectionName = "Attendance:Normalization";

    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the pause between two polls of every tenant, in seconds.</summary>
    public int PollIntervalSeconds { get; set; } = 10;
}
