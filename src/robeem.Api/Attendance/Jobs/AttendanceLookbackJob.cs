using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.Application.Tenancy;

namespace robeem.Api.Attendance.Jobs;

/// <summary>
/// The nightly look back over the last days (default 30): are the devices still sending, which scheduled days have no calculation yet
/// (the engine raises a no-show; this only hands it the days), which days still lack a check-out, and which days a leave or holiday
/// entered since changes. The logic is <see cref="IAttendanceLookbackService"/>; the job decides when.
/// </summary>
public sealed class AttendanceLookbackJob : IScheduledJob
{
    private readonly ITenantRegistry _tenants;
    private readonly ITenantScopeFactory _tenantScopes;
    private readonly AttendanceLookbackOptions _options;
    private readonly ILogger<AttendanceLookbackJob> _logger;

    public AttendanceLookbackJob(
        ITenantRegistry tenants,
        ITenantScopeFactory tenantScopes,
        IOptions<AttendanceLookbackOptions> options,
        ILogger<AttendanceLookbackJob> logger)
    {
        _tenants = tenants;
        _tenantScopes = tenantScopes;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "attendance-lookback";

    public string DisplayName => "Look-back: missing days and silent devices";

    public string Description => "Looks back over the last days: finds devices that stopped sending, scheduled days nobody punched on (a no-show, unless a leave or holiday covers it), days still missing a check-out, and days that a leave or holiday entered since changes.";

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
                _logger.LogError(ex, "The attendance look-back failed for tenant {TenantId}.", tenant.Id);
            }
        }
    }

    public async Task<JobRunResult> RunForTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        await using var scope = _tenantScopes.CreateScope(tenantId);
        var result = await scope.ServiceProvider.GetRequiredService<IAttendanceLookbackService>()
            .RunAsync(_options.EffectiveWindowDays, _options.EffectiveMaxDaysPerRun, cancellationToken);

        foreach (var device in result.SilentDevices)
        {
            _logger.LogWarning(
                "Tenant {TenantId}: no punch from device {Device} of {Integration} between {From:yyyy-MM-dd} and {To:yyyy-MM-dd}.",
                tenantId,
                device.Name,
                device.Integration,
                result.From,
                result.To);
        }

        var summary = $"{result.From:yyyy-MM-dd} to {result.To:yyyy-MM-dd}: {result.DaysCalculated} day(s) calculated"
            + (result.DaysFailed > 0 ? $", {result.DaysFailed} failed" : string.Empty)
            + (result.DaysDeferred > 0 ? $", {result.DaysDeferred} left for the next run" : string.Empty)
            + $"; {result.DevicesChecked} device(s) checked"
            + (result.SilentDevices.Count > 0 ? ", silent: " + string.Join(", ", result.SilentDevices.Select(d => $"{d.Name} ({d.Integration})")) : string.Empty)
            + (result.DatesWithoutData.Count > 0 ? $"; {result.DatesWithoutData.Count} date(s) skipped because nobody punched on them" : string.Empty)
            + ".";
        return new JobRunResult(result.DaysCalculated + result.DaysFailed + result.SilentDevices.Count, summary, result.DaysFailed > 0 || result.SilentDevices.Count > 0);
    }
}
