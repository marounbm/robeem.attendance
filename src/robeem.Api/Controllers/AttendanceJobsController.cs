using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Attendance.Jobs;
using robeem.Api.Security;
using robeem.Application.Attendance.Jobs;
using robeem.Application.Security;
using robeem.Application.Tenancy;
using robeem.Contracts.Attendance;

namespace robeem.Api.Controllers;

/// <summary>
/// The background jobs: what they do, when they run and run next, how each run ended, and Run now.
/// The history and the last run are the current tenant's; the schedule is the Api's.
/// </summary>
[ApiController]
[Authorize(Roles = AttendanceRoles.JobViewers)]
[Route("api/attendance/jobs")]
public sealed class AttendanceJobsController : ControllerBase
{
    private readonly IJobCoordinator _coordinator;
    private readonly IJobRunLog _log;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _user;

    public AttendanceJobsController(IJobCoordinator coordinator, IJobRunLog log, ITenantContext tenant, ICurrentUser user)
    {
        _coordinator = coordinator;
        _log = log;
        _tenant = tenant;
        _user = user;
    }

    [HttpGet]
    public async Task<IReadOnlyList<JobStatusItem>> List(CancellationToken cancellationToken)
    {
        var lastRuns = await _log.LastRunsAsync(cancellationToken);
        return _coordinator.Jobs
            .Select(job =>
            {
                var state = _coordinator.GetState(job.Name);
                return new JobStatusItem
                {
                    Name = job.Name,
                    DisplayName = job.DisplayName,
                    Description = job.Description,
                    Enabled = job.Enabled,
                    Schedule = job.Schedule.Description,
                    IsRunning = state?.IsRunning ?? false,
                    NextRunAt = job.Enabled ? state?.NextRunAt : null,
                    LastCheckedAt = state?.LastCheckedAt,
                    LastRun = lastRuns.GetValueOrDefault(job.Name),
                };
            })
            .ToList();
    }

    /// <summary>The run history of the current tenant, newest first; optionally one job's, or one status's.</summary>
    [HttpGet("runs")]
    public async Task<IReadOnlyList<JobRunItem>> Runs(string? jobName, string? status, int take, CancellationToken cancellationToken) =>
        await _log.ListAsync(jobName, status, take == 0 ? 100 : take, cancellationToken);

    /// <summary>Runs the job now for the current tenant, in the background. 202 with the run's id; 409 when the job is running or switched off.</summary>
    [HttpPost("{name}/run")]
    [Authorize(Roles = AttendanceRoles.IntegrationAdministrator)]
    public async Task<ActionResult<RunJobResponse>> Run(string name, CancellationToken cancellationToken)
    {
        var result = await _coordinator.StartManualAsync(name, _tenant.TenantId ?? string.Empty, _user.EmployeeId, cancellationToken);
        return result.Outcome switch
        {
            ManualRunOutcome.Started => Accepted(new RunJobResponse { RunId = result.RunId ?? 0 }),
            ManualRunOutcome.UnknownJob or ManualRunOutcome.UnknownTenant => NotFound(),
            ManualRunOutcome.Disabled => Conflict(Refusal("The job is switched off in the Api's settings.")),
            _ => Conflict(Refusal("The job is running already; wait for it to end.")),
        };
    }

    private static ProblemDetails Refusal(string detail) => new() { Title = "The job can't be started.", Detail = detail, Status = StatusCodes.Status409Conflict };
}
