using robeem.Application.Attendance;

namespace robeem.Api.Attendance.Jobs;

/// <summary>
/// Runs the <see cref="IScheduledJob"/>s in-process (the decision is recorded in CLAUDE.md §2.8).
/// Each job has its own loop, so a slow one never delays another, and runs never overlap: the next
/// run starts only after the last one ends. Run a single Api instance: there is no distributed lock.
/// </summary>
public sealed class ScheduledJobsHostedService : BackgroundService
{
    private readonly IReadOnlyList<IScheduledJob> _jobs;
    private readonly TimeProvider _clock;
    private readonly ILogger<ScheduledJobsHostedService> _logger;

    public ScheduledJobsHostedService(IEnumerable<IScheduledJob> jobs, TimeProvider clock, ILogger<ScheduledJobsHostedService> logger)
    {
        _jobs = jobs.ToList();
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var loops = new List<Task>();
        foreach (var job in _jobs)
        {
            if (!job.Enabled)
            {
                _logger.LogInformation("Job {Job} is disabled.", job.Name);
                continue;
            }

            if (job.Schedule.Cron is { } cron && !CronSchedule.TryParse(cron, out _))
            {
                _logger.LogError("Job {Job} has an invalid cron expression '{Cron}' and will not run.", job.Name, cron);
                continue;
            }

            loops.Add(RunLoopAsync(job, stoppingToken));
        }

        try
        {
            await Task.WhenAll(loops);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task RunLoopAsync(IScheduledJob job, CancellationToken stoppingToken)
    {
        if (job.Schedule.Interval is { } interval)
        {
            using var timer = new PeriodicTimer(interval, _clock);
            do
            {
                await RunOnceAsync(job, stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));

            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var next = CronSchedule.Next(job.Schedule.Cron, _clock.GetLocalNow(), _clock.LocalTimeZone);
            if (next is null)
            {
                _logger.LogWarning("Job {Job} has no next run; it stops.", job.Name);
                return;
            }

            // Task.Delay can't wait longer than about 49 days; a cron expression can be further off.
            var wait = next.Value - _clock.GetLocalNow();
            if (wait > TimeSpan.FromDays(1))
            {
                wait = TimeSpan.FromDays(1);
                await Task.Delay(wait, _clock, stoppingToken);
                continue;
            }

            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, _clock, stoppingToken);
            }

            await RunOnceAsync(job, stoppingToken);
        }
    }

    private async Task RunOnceAsync(IScheduledJob job, CancellationToken stoppingToken)
    {
        try
        {
            await job.RunAsync(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            // A failed run never ends the schedule.
            _logger.LogError(ex, "Job {Job} failed.", job.Name);
        }
    }
}
