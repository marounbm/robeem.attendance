using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Infrastructure.Seeding;

/// <summary>
/// Runs every <see cref="ISeedContributor"/> against the current tenant's database. This is the
/// consumer of the <see cref="ISeedData"/> marker: a contributor that tries to insert or change
/// an entity type without the marker is rejected and nothing it staged is saved.
/// </summary>
public sealed class SeedDataRunner
{
    private readonly RobeemDbContext _db;
    private readonly IEnumerable<ISeedContributor> _contributors;
    private readonly ILogger<SeedDataRunner> _logger;

    public SeedDataRunner(RobeemDbContext db, IEnumerable<ISeedContributor> contributors, ILogger<SeedDataRunner> logger)
    {
        _db = db;
        _contributors = contributors;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var contributor in _contributors.OrderBy(c => c.Order))
        {
            await contributor.SeedAsync(_db, cancellationToken);

            var rejected = _db.ChangeTracker.Entries()
                .Where(e => e.State != EntityState.Unchanged && e.Entity is not ISeedData)
                .Select(e => e.Metadata.ClrType.Name)
                .Distinct()
                .ToList();

            if (rejected.Count > 0)
            {
                _db.ChangeTracker.Clear();
                throw new InvalidOperationException(
                    $"{contributor.GetType().Name} tried to seed {string.Join(", ", rejected)}, which is not marked {nameof(ISeedData)}.");
            }

            var saved = await _db.SaveChangesAsync(cancellationToken);
            if (saved > 0)
            {
                _logger.LogInformation("{Contributor} seeded {Count} rows for tenant {TenantId}.", contributor.GetType().Name, saved, _db.CurrentTenantId);
            }
        }
    }
}
