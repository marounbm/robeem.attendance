using Microsoft.Extensions.DependencyInjection;
using robeem.HR.Employees;
using robeem.HR.Seeding;
using robeem.Infrastructure.Seeding;

namespace robeem.HR;

public static class HrServiceCollectionExtensions
{
    public static IServiceCollection AddRobeemHr(this IServiceCollection services)
    {
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<ISeedContributor, HrSeedContributor>();

        return services;
    }
}
