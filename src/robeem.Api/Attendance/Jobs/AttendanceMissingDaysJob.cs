using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.Application.Tenancy;

namespace robeem.Api.Attendance.Jobs;

/// <summary>
/// The nightly search for missing days over everything the system holds: from each tenant's start date (Settings → Start date) to the last day
/// attendance was entered for. The look-back only reaches the last days, so this is what raises the no-shows of a period imported later. A tenant
/// with no start date is skipped. The logic is <see cref="IAttendanceLookbackService.CheckSinceStartAsync"/>; the job decides when.
/// </summary>
public sealed class AttendanceMissingDaysJob : IScheduledJob
{
    private readonly ITenantRegistry _tenants;
    private readonly ITenantScopeFactory _tenantScopes;
    private readonly AttendanceMissingDaysOptions _options;
    private readonly ILogger<AttendanceMissingDaysJob> _logger;

    public AttendanceMissingDaysJob(
        ITenantRegistry tenants,
        ITenantScopeFactory tenantScopes,
        IOptions<AttendanceMissingDaysOptions> options,
        ILogger<AttendanceMissingDaysJob> logger)
    {
        _tenants = tenants;
        _tenantScopes = tenantScopes;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "attendance-missing-days";

    public string DisplayName => "Missing days: from the start date to the last day entered";

    public string Description => "Looks for the scheduled days nobody punched on, from the date the punching started to the last day attendance was entered for, and raises a no-show for each (unless a leave or holiday covers it). Skips a tenant with no start date.";

    public bool Enabled => _options.Enabled;

    public JobSchedule Schedule => JobSchedule.OnCron(_options.Cron);

    /// <summary>Runs for every tenant, one after the other; one tenant's failure is logged and does not stop the others.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var tenant in _tenants.Tenants)
        {
            try
            {
                await RunForTenantAsync(tenant.Id, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "The search for missing days failed for tenant {TenantId}.", tenant.Id);
            }
        }
    }

    public async Task<JobRunResult> RunForTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        await using var scope = _tenantScopes.CreateScope(tenantId);
        var result = await scope.ServiceProvider.GetRequiredService<IAttendanceLookbackService>()
            .CheckSinceStartAsync(_options.EffectiveMaxDaysPerRun, cancellationToken);
        if (result is null)
        {
            return new JobRunResult(0, "No start date is set, or no attendance has been entered yet: nothing to look at.");
        }

        var summary = $"{result.From:yyyy-MM-dd} to {result.To:yyyy-MM-dd}: {result.DaysCalculated} day(s) calculated"
            + (result.DaysFailed > 0 ? $", {result.DaysFailed} failed" : string.Empty)
            + (result.DaysDeferred > 0 ? $", {result.DaysDeferred} left for the next run" : string.Empty)
            + (result.DatesWithoutData.Count > 0 ? $"; {result.DatesWithoutData.Count} date(s) skipped because nobody punched on them" : string.Empty)
            + ".";
        return new JobRunResult(result.DaysCalculated + result.DaysFailed, summary, result.DaysFailed > 0);
    }
}
