using robeem.Api.Attendance.Jobs;

namespace robeem.Tests.Api;

/// <summary>A job whose runs the test scripts: what each run does, and what it reports.</summary>
internal sealed class StubJob : IScheduledJob
{
    private readonly object _lock = new();
    private readonly List<string> _tenants = [];
    private int _concurrent;
    private int _maxConcurrent;

    public StubJob(string name, JobSchedule schedule)
    {
        Name = name;
        Schedule = schedule;
    }

    public string Name { get; }

    public string DisplayName => Name;

    public string Description => "A stub job.";

    public bool Enabled { get; init; } = true;

    public JobSchedule Schedule { get; }

    /// <summary>Gets or sets what a run does before it reports; it gets the tenant and the token.</summary>
    public Func<string, CancellationToken, Task> Body { get; init; } = (_, _) => Task.CompletedTask;

    /// <summary>Gets or sets what a run reports, from the tenant; it may throw to fail the run.</summary>
    public Func<string, JobRunResult> Result { get; init; } = _ => JobRunResult.Idle;

    /// <summary>Gets the tenants the job was run for, in order.</summary>
    public IReadOnlyList<string> Tenants
    {
        get
        {
            lock (_lock)
            {
                return _tenants.ToList();
            }
        }
    }

    public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);

    public async Task<JobRunResult> RunForTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _tenants.Add(tenantId);
        }

        var concurrent = Interlocked.Increment(ref _concurrent);
        int seen;
        do
        {
            seen = Volatile.Read(ref _maxConcurrent);
        }
        while (concurrent > seen && Interlocked.CompareExchange(ref _maxConcurrent, concurrent, seen) != seen);

        try
        {
            await Body(tenantId, cancellationToken);
            return Result(tenantId);
        }
        finally
        {
            Interlocked.Decrement(ref _concurrent);
        }
    }
}
