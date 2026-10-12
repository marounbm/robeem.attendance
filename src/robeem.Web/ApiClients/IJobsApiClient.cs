using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's background jobs (<c>api/attendance/jobs</c>): schedule, next run, history and Run now.</summary>
public interface IJobsApiClient
{
    Task<IReadOnlyList<JobStatusItem>> ListAsync(CancellationToken cancellationToken);

    /// <param name="jobName">One job's runs; null for all.</param>
    /// <param name="status">Running, Succeeded, Failed, Missed or Cancelled; null for any.</param>
    /// <param name="take">At most this many, newest first.</param>
    Task<IReadOnlyList<JobRunItem>> ListRunsAsync(string? jobName, string? status, int take, CancellationToken cancellationToken);

    /// <summary>Starts the job now for the current tenant. Refused, with the reason, when it is running already or switched off.</summary>
    Task<ApiCommandResult<RunJobResponse>> RunAsync(string jobName, CancellationToken cancellationToken);
}
