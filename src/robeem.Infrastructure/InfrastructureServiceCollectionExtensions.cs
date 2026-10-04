using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using robeem.Infrastructure.Persistence;
using robeem.Infrastructure.Seeding;
using robeem.Infrastructure.Tenancy;

namespace robeem.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers tenancy and the per-tenant <see cref="RobeemDbContext"/>. The DbContext's
    /// connection string is chosen when it is resolved, from the scope's <see cref="TenantContext"/>;
    /// resolving it in a scope with no tenant throws <see cref="TenantNotResolvedException"/>.
    /// </summary>
    public static IServiceCollection AddRobeemInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TenancyOptions>(configuration.GetSection(TenancyOptions.SectionName));
        services.AddSingleton<ITenantRegistry, TenantRegistry>();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddSingleton<TenantScopeFactory>();

        services.AddDbContext<RobeemDbContext>((sp, options) =>
        {
            var tenant = sp.GetRequiredService<TenantContext>().Tenant ?? throw new TenantNotResolvedException();
            options.UseNpgsql(tenant.ConnectionString);
        });

        services.AddScoped<SeedDataRunner>();
        services.AddSingleton<TenantDatabaseInitializer>();

        return services;
    }
}
