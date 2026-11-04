using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using robeem.Application.Tenancy;

namespace robeem.Infrastructure.Persistence.Catalog;

/// <summary>Used by <c>dotnet ef --context CatalogDbContext</c> only; the same placeholder database as the tenant context's.</summary>
public sealed class CatalogDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(RobeemDbContextDesignTimeFactory.ConnectionStringVariable)
            ?? "Host=localhost;Database=robeem_design;Username=postgres";
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(CatalogDbContext.MigrationsHistoryTable, "Catalog"))
            .Options;

        return new CatalogDbContext(options, new DesignTimePrimeTenant());
    }

    private sealed class DesignTimePrimeTenant : IPrimeTenantContext
    {
        public string PrimeTenantId => "design-time";

        public bool IsCurrentTenantPrime => true;
    }
}
