using Microsoft.Extensions.Options;
using robeem.Api.Attendance;
using robeem.Api.Attendance.Jobs;
using robeem.Api.Security;
using robeem.Api.Tenancy;
using robeem.Application;
using robeem.Application.Attendance;
using robeem.Application.HR.Seeding;
using robeem.Infrastructure;
using robeem.Infrastructure.Attendance.Catalog;
using robeem.Infrastructure.Persistence;
using robeem.Infrastructure.Persistence.Catalog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<CatalogUnavailableExceptionHandler>();
builder.Services.AddRobeemInfrastructure(builder.Configuration);
builder.Services.AddRobeemApplication();

// After AddRobeemApplication: it replaces the system ICurrentUser with the authenticated user.
builder.Services.AddRobeemAuthentication(builder.Configuration, builder.Environment);
builder.Services.Configure<RawPayloadSanitizerOptions>(builder.Configuration.GetSection(RawPayloadSanitizerOptions.SectionName));
builder.Services.Configure<SampleDataOptions>(builder.Configuration.GetSection(SampleDataOptions.SectionName));
builder.Services.Configure<AttendanceNormalizationOptions>(builder.Configuration.GetSection(AttendanceNormalizationOptions.SectionName));
builder.Services.Configure<AttendanceSyncOptions>(builder.Configuration.GetSection(AttendanceSyncOptions.SectionName));

// The scheduler decision is in CLAUDE.md §2.8: one in-process host runs these jobs.
builder.Services.AddSingleton<IScheduledJob, NormalizationJob>();
builder.Services.AddSingleton<IScheduledJob, IntegrationSyncJob>();
builder.Services.AddSingleton<IScheduledJob, ReconciliationJob>();
builder.Services.AddSingleton<IJobRunRecorder, JobRunRecorder>();
builder.Services.AddSingleton<JobCoordinator>();
builder.Services.AddSingleton<IJobCoordinator>(services => services.GetRequiredService<JobCoordinator>());
builder.Services.AddHostedService<ScheduledJobsHostedService>();
builder.Services.AddScoped<TenantResolutionMiddleware>();

var app = builder.Build();

// Every other tenant reads the attendance catalog by calling the prime tenant's Api, and this Api answers such calls itself:
// both need the address and the service key. Fail fast, as the tenant configuration does.
var catalogErrors = app.Services.GetRequiredService<IOptions<PrimeCatalogOptions>>().Value.Validate(requireHttps: !app.Environment.IsDevelopment());
if (catalogErrors.Count > 0)
{
    throw new InvalidOperationException($"Invalid '{PrimeCatalogOptions.SectionName}' configuration: {string.Join(" ", catalogErrors)}");
}

if (app.Configuration.GetValue<bool>("Database:InitializeOnStartup"))
{
    await app.Services.GetRequiredService<TenantDatabaseInitializer>().InitializeAllAsync(app.Lifetime.ApplicationStopping);

    // The attendance catalog lives in the prime tenant's database and is migrated and seeded once, apart from the tenants' schema.
    await app.Services.GetRequiredService<CatalogDatabaseInitializer>().InitializeAsync(app.Lifetime.ApplicationStopping);
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

// Authentication comes first so the tenant can be read from the user's tenant_id claim.
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

app.MapControllers();

await app.RunAsync();
