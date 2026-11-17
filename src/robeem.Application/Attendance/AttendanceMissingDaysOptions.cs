namespace robeem.Application.Attendance;

/// <summary>Settings of the job that looks for missing days from the start date to the last day entered. Bound by the host from <see cref="SectionName"/>.</summary>
public sealed class AttendanceMissingDaysOptions
{
    public const string SectionName = "Attendance:MissingDays";

    /// <summary>Gets or sets a value indicating whether the job runs at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the schedule of the job, as a cron expression in server local time. Default: 04:00 every night, after the 03:00 look-back.</summary>
    public string Cron { get; set; } = "0 4 * * *";

    /// <summary>Gets or sets the most employee-days one run calculates; the rest wait for the next night.</summary>
    public int MaxDaysPerRun { get; set; } = 2000;

    /// <summary>Gets <see cref="MaxDaysPerRun"/> kept at 1 or more.</summary>
    public int EffectiveMaxDaysPerRun => Math.Max(1, MaxDaysPerRun);
}
