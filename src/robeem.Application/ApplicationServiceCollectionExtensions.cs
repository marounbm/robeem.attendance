using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using robeem.Application.Attendance;
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
        services.AddScoped<IIngestionService, IngestionService>();
        services.AddScoped<ISeedContributor, HrSeedContributor>();

        // Pure and stateless. Options are bound by the host (section RawPayloadSanitizerOptions.SectionName).
        services.AddOptions<RawPayloadSanitizerOptions>();
        services.AddSingleton<IRawPayloadSanitizer, RawPayloadSanitizer>();
        services.AddScoped<INormalizedTransactionRecorder, NormalizedTransactionRecorder>();
        services.AddScoped<IAttendancePolicyResolver, AttendancePolicyResolver>();
        services.AddScoped<IScheduleResolutionService, ScheduleResolutionService>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAttendanceProcessingEngine, AttendanceProcessingEngine>();
        services.AddScoped<INormalizationService, NormalizationService>();

        return services;
    }
}
