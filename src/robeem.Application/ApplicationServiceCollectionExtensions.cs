using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using robeem.Application.Attendance;
using robeem.Application.Attendance.Seeding;
using robeem.Application.HR.Employees;
using robeem.Application.HR.Seeding;
using robeem.Application.Security;
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
        services.AddScoped<ISeedContributor, AttendanceSeedContributor>();

        // Pure and stateless. Options are bound by the host (section RawPayloadSanitizerOptions.SectionName).
        services.AddOptions<RawPayloadSanitizerOptions>();
        services.AddSingleton<IRawPayloadSanitizer, RawPayloadSanitizer>();
        services.AddScoped<INormalizedTransactionRecorder, NormalizedTransactionRecorder>();
        services.AddScoped<IAttendancePolicyResolver, AttendancePolicyResolver>();
        services.AddScoped<IScheduleResolutionService, ScheduleResolutionService>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAttendanceProcessingEngine, AttendanceProcessingEngine>();
        services.AddScoped<IAttendanceCorrectionService, AttendanceCorrectionService>();
        services.AddScoped<IAttendanceReviewService, AttendanceReviewService>();
        services.AddScoped<IAttendanceExceptionQuery, AttendanceExceptionQuery>();
        services.AddScoped<IPayrollAttendanceExtract, PayrollAttendanceExtract>();

        // The actor of work no user started; the Api replaces it with the authenticated user.
        services.TryAddScoped<ICurrentUser, SystemCurrentUser>();
        services.AddScoped<IAttendanceAuditTrail, AttendanceAuditTrail>();
        services.AddScoped<INormalizationService, NormalizationService>();

        // The breaker state of every integration lives in this singleton; options are bound by the
        // host (section AttendanceSyncOptions.SectionName).
        services.AddOptions<AttendanceSyncOptions>();
        services.AddSingleton<IntegrationResilience>();
        services.AddScoped<IIntegrationSyncService, IntegrationSyncService>();
        services.AddScoped<IIntegrationHealthService, IntegrationHealthService>();

        return services;
    }
}
