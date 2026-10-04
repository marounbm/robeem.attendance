using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using robeem.Infrastructure.Tenancy;

namespace robeem.Infrastructure.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c> only. Migrations are tenant-agnostic (every tenant database has the
/// same schema), so a placeholder tenant and database are enough to build the model.
/// </summary>
public sealed class RobeemDbContextDesignTimeFactory : IDesignTimeDbContextFactory<RobeemDbContext>
{
    public const string ConnectionStringVariable = "ROBEEM_DESIGN_CONNECTION";

    private const string DefaultConnectionString =
        "Host=localhost;Database=robeem_design;Username=postgres";

    public RobeemDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable) ?? DefaultConnectionString;
        var options = new DbContextOptionsBuilder<RobeemDbContext>().UseNpgsql(connectionString).Options;

        return new RobeemDbContext(options, new FixedTenantContext("design-time"));
    }
}
