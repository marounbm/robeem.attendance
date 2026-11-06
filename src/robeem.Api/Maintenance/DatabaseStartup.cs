using robeem.Infrastructure.Persistence;
using robeem.Infrastructure.Persistence.Catalog;

namespace robeem.Api.Maintenance;

/// <summary>Migrates and seeds every database, and puts the Api in maintenance mode when a database cannot be reached.</summary>
public sealed class DatabaseStartup
{
    private readonly TenantDatabaseInitializer _tenants;
    private readonly CatalogDatabaseInitializer _catalog;
    private readonly DatabaseReadiness _readiness;
    private readonly ILogger<DatabaseStartup> _logger;

    public DatabaseStartup(TenantDatabaseInitializer tenants, CatalogDatabaseInitializer catalog, DatabaseReadiness readiness, ILogger<DatabaseStartup> logger)
    {
        _tenants = tenants;
        _catalog = catalog;
        _readiness = readiness;
        _logger = logger;
    }

    /// <summary>
    /// Runs the initializers. Returns false, leaving the Api in maintenance mode, when a database could not be reached; any
    /// other failure (a bad migration, a seed that was refused) is a fault and is thrown, as before.
    /// </summary>
    public async Task<bool> TryInitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _tenants.InitializeAllAsync(cancellationToken);

            // The attendance catalog lives in the prime tenant's database and is migrated and seeded once, apart from the tenants' schema.
            await _catalog.InitializeAsync(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && DatabaseConnectivity.IsUnreachable(ex))
        {
            _logger.LogError(ex, "A database is not reachable. The Api is in maintenance mode and will try again.");
            return false;
        }

        _readiness.MarkReady();
        _logger.LogInformation("The databases are ready.");
        return true;
    }
}
