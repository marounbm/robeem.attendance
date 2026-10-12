namespace robeem.Api.Attendance.Jobs;

/// <summary>When a job runs: every <see cref="Interval"/> (first run at once), or at the times of a <see cref="Cron"/> expression in server local time.</summary>
public sealed record JobSchedule
{
    private JobSchedule()
    {
    }

    public TimeSpan? Interval { get; private init; }

    public string? Cron { get; private init; }

    /// <summary>Gets the schedule in words, for the jobs screen.</summary>
    public string Description => Interval is { } interval
        ? interval.TotalSeconds < 60 ? $"Every {interval.TotalSeconds:0.#} seconds" : interval.TotalMinutes < 60 ? $"Every {interval.TotalMinutes:0.#} minutes" : $"Every {interval.TotalHours:0.#} hours"
        : $"Cron {Cron} (server local time)";

    public static JobSchedule Every(TimeSpan interval) => new() { Interval = interval };

    public static JobSchedule OnCron(string cron) => new() { Cron = cron };
}
