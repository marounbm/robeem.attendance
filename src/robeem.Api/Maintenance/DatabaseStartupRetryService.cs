namespace robeem.Api.Maintenance;

/// <summary>While the Api is in maintenance mode, tries to migrate and seed the databases again until it succeeds.</summary>
public sealed class DatabaseStartupRetryService : BackgroundService
{
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(15);

    private readonly DatabaseStartup _startup;
    private readonly DatabaseReadiness _readiness;

    public DatabaseStartupRetryService(DatabaseStartup startup, DatabaseReadiness readiness)
    {
        _startup = startup;
        _readiness = readiness;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(RetryInterval);
            while (!_readiness.IsReady && await timer.WaitForNextTickAsync(stoppingToken))
            {
                await _startup.TryInitializeAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }
}
