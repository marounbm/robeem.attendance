namespace robeem.Api.Attendance.Jobs;

/// <summary>When a job runs: every <see cref="Interval"/> (first run at once), or at the times of a <see cref="Cron"/> expression in server local time.</summary>
public sealed record JobSchedule
{
    private JobSchedule()
    {
    }

    public TimeSpan? Interval { get; private init; }

    public string? Cron { get; private init; }

    public static JobSchedule Every(TimeSpan interval) => new() { Interval = interval };

    public static JobSchedule OnCron(string cron) => new() { Cron = cron };
}
