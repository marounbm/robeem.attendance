using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using robeem.Application.Tenancy;
using robeem.Infrastructure.Seeding;

namespace robeem.Infrastructure.Persistence;

/// <summary>Applies pending migrations and seed data to every configured tenant database.</summary>
public sealed class TenantDatabaseInitializer
{
    private readonly ITenantRegistry _registry;
    private readonly ITenantScopeFactory _tenantScopes;
    private readonly ILogger<TenantDatabaseInitializer> _logger;

    public TenantDatabaseInitializer(ITenantRegistry registry, ITenantScopeFactory tenantScopes, ILogger<TenantDatabaseInitializer> logger)
    {
        _registry = registry;
        _tenantScopes = tenantScopes;
        _logger = logger;
    }

    public async Task InitializeAllAsync(CancellationToken cancellationToken)
    {
        foreach (var tenant in _registry.Tenants)
        {
            await using var scope = _tenantScopes.CreateScope(tenant.Id);

            var db = scope.ServiceProvider.GetRequiredService<RobeemDbContext>();
            await db.Database.MigrateAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<SeedDataRunner>().RunAsync(cancellationToken);

            _logger.LogInformation("Tenant database for {TenantId} is up to date.", tenant.Id);
        }
    }
}
