using Microsoft.Extensions.Options;
using robeem.Web.Actors;
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
        services.AddTransient<ActorHeaderHandler>();

        AddClient<IEmployeesApiClient, EmployeesApiClient>(services);
        AddClient<ITenantsApiClient, TenantsApiClient>(services);
        AddClient<IActorsApiClient, ActorsApiClient>(services);
        AddClient<IExceptionsApiClient, ExceptionsApiClient>(services);
        AddClient<ICorrectionsApiClient, CorrectionsApiClient>(services);
        AddClient<IReviewsApiClient, ReviewsApiClient>(services);
        AddClient<IIntegrationsApiClient, IntegrationsApiClient>(services);
        AddClient<IPayPeriodsApiClient, PayPeriodsApiClient>(services);
        AddClient<IPayrollApiClient, PayrollApiClient>(services);
        AddClient<IPoliciesApiClient, PoliciesApiClient>(services);
        AddClient<IAuditApiClient, AuditApiClient>(services);
        AddClient<IRecordsApiClient, RecordsApiClient>(services);

        return services;
    }

    /// <summary>
    /// Registers one typed client. Every client carries the picked tenant and, in Development, the
    /// "act as" choice. A new area's client is one line in <see cref="AddRobeemApiClients"/>.
    /// </summary>
    private static void AddClient<TClient, TImplementation>(IServiceCollection services)
        where TClient : class
        where TImplementation : class, TClient
        => services.AddHttpClient<TClient, TImplementation>(ConfigureBaseAddress)
            .AddHttpMessageHandler<TenantHeaderHandler>()
            .AddHttpMessageHandler<ActorHeaderHandler>();

    private static void ConfigureBaseAddress(IServiceProvider services, HttpClient client)
        => client.BaseAddress = services.GetRequiredService<IOptions<RobeemApiOptions>>().Value.BaseUri;
}
