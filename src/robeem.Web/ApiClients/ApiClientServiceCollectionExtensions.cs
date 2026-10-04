using Microsoft.Extensions.Options;
using robeem.Web.Tenancy;

namespace robeem.Web.ApiClients;

public static class ApiClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers the typed robeem.Api clients. Every request carries the tenant picked in the
    /// front end (<see cref="TenantHeaderHandler"/>). A missing or relative base URL fails at startup.
    /// </summary>
    public static IServiceCollection AddRobeemApiClients(this IServiceCollection services)
    {
        services.AddOptions<RobeemApiOptions>()
            .BindConfiguration(RobeemApiOptions.SectionName)
            .Validate(
                o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
                $"'{RobeemApiOptions.SectionName}:{nameof(RobeemApiOptions.BaseUrl)}' must be an absolute http(s) URL.")
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddTransient<TenantHeaderHandler>();

        services.AddHttpClient<IEmployeesApiClient, EmployeesApiClient>(ConfigureBaseAddress)
            .AddHttpMessageHandler<TenantHeaderHandler>();
        services.AddHttpClient<ITenantsApiClient, TenantsApiClient>(ConfigureBaseAddress)
            .AddHttpMessageHandler<TenantHeaderHandler>();

        return services;
    }

    private static void ConfigureBaseAddress(IServiceProvider services, HttpClient client)
        => client.BaseAddress = services.GetRequiredService<IOptions<RobeemApiOptions>>().Value.BaseUri;
}
