using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using robeem.Application.Tenancy;
using robeem.Infrastructure.Seeding;

namespace robeem.Infrastructure.Persistence.Catalog;

/// <summary>
/// Applies the catalog's migrations to the prime tenant's database and seeds the platform catalog, once. Runs after
/// <see cref="TenantDatabaseInitializer"/>, in a scope of the prime tenant, which is the only scope that may write the catalog.
/// </summary>
public sealed class CatalogDatabaseInitializer
{
    private readonly ITenantRegistry _registry;
    private readonly ITenantScopeFactory _tenantScopes;
    private readonly ILogger<CatalogDatabaseInitializer> _logger;

    public CatalogDatabaseInitializer(ITenantRegistry registry, ITenantScopeFactory tenantScopes, ILogger<CatalogDatabaseInitializer> logger)
    {
        _registry = registry;
        _tenantScopes = tenantScopes;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var prime = _registry.PrimeTenant;
        await using var scope = _tenantScopes.CreateScope(prime.Id);

        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<CatalogSeedRunner>().RunAsync(cancellationToken);

        _logger.LogInformation("The attendance catalog in the database of prime tenant {TenantId} is up to date.", prime.Id);
    }
}
