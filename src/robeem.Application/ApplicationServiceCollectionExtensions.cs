using Microsoft.Extensions.DependencyInjection;
using robeem.Application.HR.Employees;
using robeem.Application.HR.Seeding;
using robeem.Application.Seeding;

namespace robeem.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>Registers the application services. Requires <c>AddRobeemInfrastructure</c> for <see cref="Persistence.IRobeemDbContext"/>.</summary>
    public static IServiceCollection AddRobeemApplication(this IServiceCollection services)
    {
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<ISeedContributor, HrSeedContributor>();

        return services;
    }
}
