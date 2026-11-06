namespace robeem.Api.Maintenance;

/// <summary>
/// Whether the tenant databases have been migrated and seeded since the Api started. Until they have (the database was not
/// reachable at startup) the Api is in maintenance mode: it answers, but tenant requests get a 503 and the background jobs wait.
/// </summary>
public sealed class DatabaseReadiness
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DatabaseReadiness(bool ready = false)
    {
        if (ready)
        {
            MarkReady();
        }
    }

    public bool IsReady => _ready.Task.IsCompleted;

    public void MarkReady() => _ready.TrySetResult();

    public Task WaitUntilReadyAsync(CancellationToken cancellationToken) => _ready.Task.WaitAsync(cancellationToken);
}
