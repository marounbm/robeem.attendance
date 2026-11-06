using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using robeem.Application.Attendance;
using robeem.Application.Attendance.Catalog;
using robeem.Application.Attendance.Jobs;
using robeem.Application.Attendance.Seeding;
using robeem.Application.Common;
using robeem.Application.HR.Employees;
using robeem.Application.HR.Schedules;
using robeem.Application.HR.Seeding;
using robeem.Application.Security;
using robeem.Application.Seeding;
using robeem.Application.Tenancy;

namespace robeem.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>Registers the application services. Requires <c>AddRobeemInfrastructure</c> for <see cref="Persistence.IRobeemDbContext"/>.</summary>
    public static IServiceCollection AddRobeemApplication(this IServiceCollection services)
    {
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<ITimeScheduleService, TimeScheduleService>();
        services.AddScoped<IChangeAuditTrail, AttendanceChangeAuditTrail>();
        services.AddScoped<IIngestionService, IngestionService>();
        services.AddScoped<IWebhookReceiver, WebhookReceiver>();
        services.AddScoped<ISeedContributor, HrSeedContributor>();
        services.AddOptions<SampleDataOptions>();
        services.AddScoped<ISeedContributor, ProfileSeedContributor>();
        services.AddScoped<ISeedContributor, AttendanceSeedContributor>();
        services.AddScoped<ICatalogSeedContributor, CatalogSeedContributor>();

        // How tenant code reads the prime tenant's catalog, and who may change it. The prime tenant's own scope reads the catalog
        // database (CatalogReader, which the prime Api's catalog endpoints also use). Every other tenant's scope asks the prime Api
        // (IRemoteCatalogReader, implemented in Infrastructure). The choice is made when the scope first resolves the reader,
        // by which tenant the scope is for, as RobeemDbContext does for its database.
        services.AddScoped<CatalogReader>();
        services.AddScoped<ICatalogReader>(sp => sp.GetRequiredService<IPrimeTenantContext>().IsCurrentTenantPrime
            ? sp.GetRequiredService<CatalogReader>()
            : sp.GetRequiredService<IRemoteCatalogReader>());
        services.AddScoped<ICatalogWriteAccess, CatalogWriteAccess>();

        // Pure and stateless. Options are bound by the host (section RawPayloadSanitizerOptions.SectionName).
        services.AddOptions<RawPayloadSanitizerOptions>();
        services.AddSingleton<IRawPayloadSanitizer, RawPayloadSanitizer>();
        services.AddScoped<INormalizedTransactionRecorder, NormalizedTransactionRecorder>();
        services.AddScoped<IAttendancePolicyResolver, AttendancePolicyResolver>();
        services.AddScoped<IScheduleResolutionService, ScheduleResolutionService>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAttendanceProcessingEngine, AttendanceProcessingEngine>();
        services.AddScoped<IPendingDayProcessor, PendingDayProcessor>();
        services.AddScoped<IJobRunLog, JobRunLog>();
        services.AddScoped<IAttendanceApprovalService, AttendanceApprovalService>();
        services.AddScoped<IAttendanceApprovalReconciler, AttendanceApprovalReconciler>();
        services.AddScoped<IAttendanceCorrectionService, AttendanceCorrectionService>();
        services.AddScoped<IAttendanceReviewService, AttendanceReviewService>();
        services.AddScoped<IAttendanceExceptionService, AttendanceExceptionService>();
        services.AddScoped<IAttendanceDayCorrectionService, AttendanceDayCorrectionService>();
        services.AddScoped<IAttendanceExceptionQuery, AttendanceExceptionQuery>();
        services.AddScoped<IAttendanceTransactionQuery, AttendanceTransactionQuery>();
        services.AddScoped<IAttendanceIntegrationService, AttendanceIntegrationService>();
        services.AddScoped<IAttendancePayloadFormatService, AttendancePayloadFormatService>();
        services.AddScoped<IAttendanceVendorService, AttendanceVendorService>();
        services.AddScoped<IAttendanceConnectorService, AttendanceConnectorService>();
        services.AddScoped<IAttendanceConnectionService, AttendanceConnectionService>();
        services.AddSingleton<IPayloadDecryptorCatalog, PayloadDecryptorCatalog>();
        services.AddScoped<IAttendanceDeviceTypeService, AttendanceDeviceTypeService>();
        services.AddScoped<IAttendanceTransportService, AttendanceTransportService>();
        services.AddScoped<IPayPeriodService, PayPeriodService>();
        services.AddScoped<IAttendancePolicyService, AttendancePolicyService>();
        services.AddScoped<IAttendanceRecordService, AttendanceRecordService>();
        services.AddScoped<IPayrollAttendanceExtract, PayrollAttendanceExtract>();
        services.AddScoped<IPayrollApprovedPageService, PayrollApprovedPageService>();

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
