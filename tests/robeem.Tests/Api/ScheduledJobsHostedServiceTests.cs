using Microsoft.Extensions.Logging.Abstractions;
using robeem.Api.Attendance.Jobs;

namespace robeem.Tests.Api;

/// <summary>
/// The host that runs the jobs (P10): the schedule of each job, and that one job's failure, slowness
/// or invalid setting never touches another. It runs on the real clock with intervals of a few
/// milliseconds, and waits for conditions rather than for fixed times.
/// </summary>
public sealed class ScheduledJobsHostedServiceTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task AnIntervalJob_RunsAtOnce_AndAgainAndAgain()
    {
        var job = new FakeJob("tick", JobSchedule.Every(TimeSpan.FromMilliseconds(20)));

        await using var host = await Host.StartAsync(job);

        await WaitUntil(() => job.Runs >= 3);
    }

    [Fact]
    public async Task ACronJob_RunsAtItsTimes()
    {
        // Every second: the six-field form puts seconds first.
        var job = new FakeJob("cron", JobSchedule.OnCron("* * * * * *"));

        await using var host = await Host.StartAsync(job);

        await WaitUntil(() => job.Runs >= 2);
    }

    [Fact]
    public async Task ADisabledJob_NeverRuns_AndTheOthersDo()
    {
        var disabled = new FakeJob("off", JobSchedule.Every(TimeSpan.FromMilliseconds(10))) { Enabled = false };
        var enabled = new FakeJob("on", JobSchedule.Every(TimeSpan.FromMilliseconds(10)));

        await using var host = await Host.StartAsync(disabled, enabled);

        await WaitUntil(() => enabled.Runs >= 3);
        Assert.Equal(0, disabled.Runs);
    }

    [Fact]
    public async Task AJobWithAnInvalidCron_IsSkipped_AndTheOthersRun()
    {
        var invalid = new FakeJob("bad", JobSchedule.OnCron("not a cron"));
        var valid = new FakeJob("good", JobSchedule.Every(TimeSpan.FromMilliseconds(10)));

        await using var host = await Host.StartAsync(invalid, valid);

        await WaitUntil(() => valid.Runs >= 3);
        Assert.Equal(0, invalid.Runs);
    }

    [Fact]
    public async Task AJobThatThrows_KeepsItsSchedule()
    {
        var job = new FakeJob("flaky", JobSchedule.Every(TimeSpan.FromMilliseconds(10)))
        {
            Body = (run, _) => run <= 2 ? throw new InvalidOperationException("boom") : Task.CompletedTask,
        };

        await using var host = await Host.StartAsync(job);

        // Two failed runs, then it carries on.
        await WaitUntil(() => job.Runs >= 5);
    }

    [Fact]
    public async Task ARunNeverOverlapsTheNextOneOfTheSameJob()
    {
        var job = new FakeJob("slow", JobSchedule.Every(TimeSpan.FromMilliseconds(5)))
        {
            Body = async (_, token) => await Task.Delay(40, token),
        };

        await using var host = await Host.StartAsync(job);

        await WaitUntil(() => job.Runs >= 4);
        Assert.Equal(1, job.MaxConcurrent);
    }

    [Fact]
    public async Task ASlowJob_DoesNotDelayAnother()
    {
        var gate = new TaskCompletionSource();
        var slow = new FakeJob("blocked", JobSchedule.Every(TimeSpan.FromMilliseconds(10))) { Body = async (_, _) => await gate.Task };
        var quick = new FakeJob("quick", JobSchedule.Every(TimeSpan.FromMilliseconds(10)));

        await using var host = await Host.StartAsync(slow, quick);

        // The first job is stuck inside its first run; the second goes on.
        await WaitUntil(() => quick.Runs >= 5);
        Assert.Equal(1, slow.Runs);
        gate.SetResult();
    }

    [Fact]
    public async Task Stopping_CancelsARunInProgress_Promptly()
    {
        var started = new TaskCompletionSource();
        var job = new FakeJob("long", JobSchedule.Every(TimeSpan.FromMilliseconds(10)))
        {
            Body = async (_, token) =>
            {
                started.TrySetResult();
                await Task.Delay(TimeSpan.FromMinutes(5), token);
            },
        };
        var host = await Host.StartAsync(job);
        await started.Task.WaitAsync(Patience);

        var stopping = host.StopAsync();

        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(job.Cancelled, "The run in progress was not cancelled.");
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Patience);
        while (!condition())
        {
            if (timeout.IsCancellationRequested)
            {
                Assert.Fail("The condition was not met in time.");
            }

            await Task.Delay(5);
        }
    }

    private sealed class Host : IAsyncDisposable
    {
        private readonly ScheduledJobsHostedService _service;
        private bool _stopped;

        private Host(ScheduledJobsHostedService service)
        {
            _service = service;
        }

        public static async Task<Host> StartAsync(params FakeJob[] jobs)
        {
            var service = new ScheduledJobsHostedService(jobs, TimeProvider.System, NullLogger<ScheduledJobsHostedService>.Instance);
            await service.StartAsync(CancellationToken.None);
            return new Host(service);
        }

        public async Task StopAsync()
        {
            _stopped = true;
            await _service.StopAsync(CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            if (!_stopped)
            {
                await StopAsync();
            }

            _service.Dispose();
        }
    }

    private sealed class FakeJob : IScheduledJob
    {
        private int _runs;
        private int _concurrent;
        private int _maxConcurrent;
        private int _cancelled;

        public FakeJob(string name, JobSchedule schedule)
        {
            Name = name;
            Schedule = schedule;
        }

        public string Name { get; }

        public bool Enabled { get; init; } = true;

        public JobSchedule Schedule { get; }

        /// <summary>Gets or sets what a run does; it gets the number of the run, from 1.</summary>
        public Func<int, CancellationToken, Task> Body { get; init; } = (_, _) => Task.CompletedTask;

        public int Runs => Volatile.Read(ref _runs);

        public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);

        public bool Cancelled => Volatile.Read(ref _cancelled) > 0;

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var run = Interlocked.Increment(ref _runs);
            var concurrent = Interlocked.Increment(ref _concurrent);
            InterlockedMax(ref _maxConcurrent, concurrent);
            try
            {
                await Body(run, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _cancelled);
                throw;
            }
            finally
            {
                Interlocked.Decrement(ref _concurrent);
            }
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            do
            {
                current = Volatile.Read(ref target);
            }
            while (value > current && Interlocked.CompareExchange(ref target, value, current) != current);
        }
    }
}
