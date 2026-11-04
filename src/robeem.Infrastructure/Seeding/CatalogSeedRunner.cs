using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using robeem.Application.Seeding;
using robeem.DataModel;
using robeem.Infrastructure.Persistence.Catalog;

namespace robeem.Infrastructure.Seeding;

/// <summary>
/// Runs every <see cref="ICatalogSeedContributor"/> against the catalog. The catalog counterpart of <see cref="SeedDataRunner"/>: a
/// contributor that tries to insert or change an entity type without the <see cref="ISeedData"/> marker is rejected and nothing it
/// staged is saved. The scope must be the prime tenant's, or the catalog refuses the save.
/// </summary>
public sealed class CatalogSeedRunner
{
    private readonly CatalogDbContext _catalog;
    private readonly IEnumerable<ICatalogSeedContributor> _contributors;
    private readonly ILogger<CatalogSeedRunner> _logger;

    public CatalogSeedRunner(CatalogDbContext catalog, IEnumerable<ICatalogSeedContributor> contributors, ILogger<CatalogSeedRunner> logger)
    {
        _catalog = catalog;
        _contributors = contributors;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var contributor in _contributors.OrderBy(c => c.Order))
        {
            await contributor.SeedAsync(_catalog, cancellationToken);

            var rejected = _catalog.ChangeTracker.Entries()
                .Where(e => e.State != EntityState.Unchanged && e.Entity is not ISeedData)
                .Select(e => e.Metadata.ClrType.Name)
                .Distinct()
                .ToList();

            if (rejected.Count > 0)
            {
                _catalog.ChangeTracker.Clear();
                throw new InvalidOperationException(
                    $"{contributor.GetType().Name} tried to seed {string.Join(", ", rejected)}, which is not marked {nameof(ISeedData)}.");
            }

            var saved = await _catalog.SaveChangesAsync(cancellationToken);
            if (saved > 0)
            {
                _logger.LogInformation("{Contributor} seeded {Count} catalog rows.", contributor.GetType().Name, saved);
            }
        }
    }
}
