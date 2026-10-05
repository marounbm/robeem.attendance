using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using robeem.Application.Attendance;
using robeem.Application.Persistence;
using robeem.Application.Security;
using robeem.Application.Tenancy;
using robeem.Infrastructure.Attendance.Providers;
using robeem.Infrastructure.Persistence;
using robeem.Infrastructure.Security;
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
        services.AddSingleton<ITenantScopeFactory>(sp => sp.GetRequiredService<TenantScopeFactory>());

        services.AddDbContext<RobeemDbContext>((sp, options) =>
        {
            var tenant = sp.GetRequiredService<TenantContext>().Tenant ?? throw new TenantNotResolvedException();
            options.UseNpgsql(tenant.ConnectionString);
        });

        services.AddScoped<IRobeemDbContext>(sp => sp.GetRequiredService<RobeemDbContext>());

        services.AddScoped<ISecretStore, ConfigurationSecretStore>();
        services.AddScoped<SeedDataRunner>();
        services.AddSingleton<TenantDatabaseInitializer>();

        // Attendance adapters: one keyed service per AttendanceProvider.ProviderKey (CLAUDE.md §1.9).
        // A new vendor is an adapter plus an event interpreter, registered under the same key.
        services.AddKeyedSingleton<IAttendanceProvider, GenericCsvProvider>(GenericCsvProvider.ProviderKey);
        services.AddKeyedSingleton<IAttendanceEventInterpreter, GenericCsvInterpreter>(GenericCsvProvider.ProviderKey);

        return services;
    }
}
