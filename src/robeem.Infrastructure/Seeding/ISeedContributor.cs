using robeem.Infrastructure.Persistence;

namespace robeem.Infrastructure.Seeding;

/// <summary>
/// Adds reference data to a tenant database. Contributors must be idempotent: they run every
/// time a tenant database is initialized and must only add what is missing. They may only add
/// entities marked with <see cref="robeem.DataModel.ISeedData"/>.
/// </summary>
public interface ISeedContributor
{
    /// <summary>Gets the run order; lower runs first.</summary>
    int Order { get; }

    Task SeedAsync(RobeemDbContext db, CancellationToken cancellationToken);
}
