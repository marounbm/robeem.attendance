namespace robeem.Application.Attendance;

/// <summary>Settings of the nightly look-back job (no-shows, silent devices, leave re-read). Bound by the host from <see cref="SectionName"/>.</summary>
public sealed class AttendanceLookbackOptions
{
    public const string SectionName = "Attendance:Lookback";

    /// <summary>Gets or sets a value indicating whether the look-back job runs at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the schedule of the job, as a cron expression in server local time. Default: 03:00 every night, after the 02:00 reconciliation has brought late punches in.</summary>
    public string Cron { get; set; } = "0 3 * * *";

    /// <summary>Gets or sets how many days back the job looks, ending yesterday. Used between 1 and 365.</summary>
    public int WindowDays { get; set; } = 30;

    /// <summary>Gets or sets the most employee-days one run calculates again; the rest wait for the next night.</summary>
    public int MaxDaysPerRun { get; set; } = 2000;

    /// <summary>Gets <see cref="WindowDays"/> kept between 1 and 365.</summary>
    public int EffectiveWindowDays => Math.Clamp(WindowDays, 1, 365);

    /// <summary>Gets <see cref="MaxDaysPerRun"/> kept at 1 or more.</summary>
    public int EffectiveMaxDaysPerRun => Math.Max(1, MaxDaysPerRun);
}
