using robeem.Api.Attendance;
using robeem.Api.Attendance.Jobs;
using robeem.Api.Security;
using robeem.Api.Tenancy;
using robeem.Application;
using robeem.Application.Attendance;
using robeem.Infrastructure;
using robeem.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddRobeemInfrastructure(builder.Configuration);
builder.Services.AddRobeemApplication();

// After AddRobeemApplication: it replaces the system ICurrentUser with the authenticated user.
builder.Services.AddRobeemAuthentication(builder.Configuration, builder.Environment);
builder.Services.Configure<RawPayloadSanitizerOptions>(builder.Configuration.GetSection(RawPayloadSanitizerOptions.SectionName));
builder.Services.Configure<AttendanceNormalizationOptions>(builder.Configuration.GetSection(AttendanceNormalizationOptions.SectionName));
builder.Services.Configure<AttendanceSyncOptions>(builder.Configuration.GetSection(AttendanceSyncOptions.SectionName));

// The scheduler decision is in CLAUDE.md §2.8: one in-process host runs these jobs.
builder.Services.AddSingleton<IScheduledJob, NormalizationJob>();
builder.Services.AddSingleton<IScheduledJob, IntegrationSyncJob>();
builder.Services.AddSingleton<IScheduledJob, ReconciliationJob>();
builder.Services.AddHostedService<ScheduledJobsHostedService>();
builder.Services.AddScoped<TenantResolutionMiddleware>();

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:InitializeOnStartup"))
{
    await app.Services.GetRequiredService<TenantDatabaseInitializer>().InitializeAllAsync(app.Lifetime.ApplicationStopping);
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
