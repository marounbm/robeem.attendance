using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using robeem.Application.Attendance;
using robeem.Application.Persistence;
using robeem.Application.Security;
using robeem.Application.Tenancy;
using robeem.Infrastructure.Attendance;
using robeem.Infrastructure.Attendance.Decryption;
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

        // Attendance adapters: one keyed service per AttendanceVendor.VendorKey (CLAUDE.md §1.9). Only a vendor
        // that is not read from files needs one (an API it is pulled from, a webhook it pushes): an adapter plus an
        // event interpreter, registered under the same key. A vendor of exported files is a file format in the database.
        services.AddSingleton<IAttendanceAdapterCatalog, AttendanceAdapterCatalog>();

        // BioStar is pulled over HTTPS. The second client is for a server with a self-signed certificate and is
        // used only when the integration's secret says allowUntrustedCertificate: certificates are verified otherwise.
        services.AddHttpClient(SupremaBiostarProvider.HttpClientName);
        services.AddHttpClient(SupremaBiostarProvider.UntrustedHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator });
        services.TryAddSingleton(TimeProvider.System);
        services.AddKeyedSingleton<IAttendanceAdapter, SupremaBiostarProvider>(SupremaBiostarProvider.VendorKey);
        services.AddKeyedSingleton<IAttendanceEventInterpreter, SupremaBiostarInterpreter>(SupremaBiostarProvider.VendorKey);
        services.AddKeyedSingleton<IAttendanceAdapter, GenericWebhookProvider>(GenericWebhookProvider.VendorKey);
        services.AddKeyedSingleton<IAttendanceEventInterpreter, GenericWebhookInterpreter>(GenericWebhookProvider.VendorKey);

        // Custom file formats: an administrator's own layout, read and interpreted without a vendor adapter.
        services.AddSingleton<ICustomFormatFileReader, CustomFormatFileReader>();
        services.AddSingleton<ICustomFormatInterpreterFactory, CustomFormatInterpreterFactory>();

        // Decryption of a connector's encrypted payload, one decryptor per PayloadEncryptionKind. The keys come from ISecretStore.
        services.AddSingleton<IPayloadDecryptor, AesPayloadDecryptor>();
        services.AddSingleton<IPayloadDecryptor, PgpPayloadDecryptor>();
        services.AddSingleton<IPayloadDecryptor, PasswordZipPayloadDecryptor>();

        return services;
    }
}
