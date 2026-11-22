using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's loading of test employees from a CSV file (<c>api/attendance/test-employees</c>).</summary>
public interface ITestEmployeesApiClient
{
    Task<TestEmployeeSummary> GetSummaryAsync(CancellationToken cancellationToken);

    /// <summary>Loads the file. Refused, with the reason or every wrong row, when the host doesn't allow it or any row is wrong; nothing is loaded then.</summary>
    Task<ApiCommandResult<TestEmployeeLoadResult>> LoadAsync(Stream file, string fileName, string timeZone, CancellationToken cancellationToken);

    /// <summary>What deleting the file's employees and their data would delete. Nothing is deleted.</summary>
    Task<ApiCommandResult<TestEmployeeDeleteResult>> PreviewDeleteAsync(Stream file, string fileName, CancellationToken cancellationToken);

    /// <summary>Deletes the file's employees and everything of theirs. Refused, with the reason, when the host doesn't allow it, the tenant id was not typed, or the file is wrong.</summary>
    Task<ApiCommandResult<TestEmployeeDeleteResult>> DeleteAsync(Stream file, string fileName, string confirmation, CancellationToken cancellationToken);
}
