using robeem.Application.Attendance.Jobs;
using robeem.Application.Tenancy;
using robeem.DataModel;

namespace robeem.Api.Attendance.Jobs;

/// <summary>Records the history through <see cref="IJobRunLog"/> in the tenant's own scope.</summary>
public sealed class JobRunRecorder : IJobRunRecorder
{
    private readonly ITenantScopeFactory _tenantScopes;
    private readonly ILogger<JobRunRecorder> _logger;

    public JobRunRecorder(ITenantScopeFactory tenantScopes, ILogger<JobRunRecorder> logger)
    {
        _tenantScopes = tenantScopes;
        _logger = logger;
    }

    public async Task<int?> BeginAsync(string tenantId, string jobName, JobTrigger trigger, JobRunStatus status, DateTime? scheduledFor, DateTime startedAt, DateTime? completedAt, string? summary, int? requestedById, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _tenantScopes.CreateScope(tenantId);
            return await scope.ServiceProvider.GetRequiredService<IJobRunLog>().BeginAsync(jobName, trigger, status, scheduledFor, startedAt, completedAt, summary, requestedById, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Could not record a run of job {Job} for tenant {TenantId}.", jobName, tenantId);
            return null;
        }
    }

    public async Task CompleteAsync(string tenantId, int runId, JobRunStatus status, int workItems, string? summary, DateTime completedAt, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _tenantScopes.CreateScope(tenantId);
            await scope.ServiceProvider.GetRequiredService<IJobRunLog>().CompleteAsync(runId, status, workItems, summary, completedAt, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Could not settle job run {RunId} of tenant {TenantId}.", runId, tenantId);
        }
    }

    public async Task<int> FailInterruptedAsync(string tenantId, DateTime now, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _tenantScopes.CreateScope(tenantId);
            return await scope.ServiceProvider.GetRequiredService<IJobRunLog>().FailInterruptedAsync(now, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Could not settle the interrupted job runs of tenant {TenantId}.", tenantId);
            return 0;
        }
    }

    public async Task<DateTime?> LastScheduledAtAsync(string tenantId, string jobName, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _tenantScopes.CreateScope(tenantId);
            return await scope.ServiceProvider.GetRequiredService<IJobRunLog>().LastScheduledAtAsync(jobName, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Could not read the history of job {Job} for tenant {TenantId}.", jobName, tenantId);
            return null;
        }
    }
}
