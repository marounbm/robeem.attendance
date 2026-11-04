using robeem.Application.Persistence;

namespace robeem.Application.Seeding;

/// <summary>
/// Adds platform data to the attendance catalog. Like <see cref="ISeedContributor"/>, but it runs once, against the prime tenant's
/// database, and sees the catalog rather than a tenant's data. It must be idempotent: only add what is missing, and never overwrite
/// what an administrator changed. Every entity it stages a change to must be marked <c>ISeedData</c>.
/// </summary>
public interface ICatalogSeedContributor
{
    /// <summary>Gets the order contributors run in, lowest first.</summary>
    int Order { get; }

    Task SeedAsync(ICatalogDbContext db, CancellationToken cancellationToken);
}
