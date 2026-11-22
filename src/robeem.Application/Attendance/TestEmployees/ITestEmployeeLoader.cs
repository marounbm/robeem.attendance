using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance.TestEmployees;

/// <summary>
/// Loads test employees from a CSV file: the employees, the time schedule and team of each, and the mapping of each one's number to a device, so
/// that a file of punches made for them is found at once. The file is checked whole first; nothing is made unless every row is right.
/// </summary>
public interface ITestEmployeeLoader
{
    Task<TestEmployeeSummary> GetSummaryAsync(CancellationToken cancellationToken);

    /// <param name="csv">The file.</param>
    /// <param name="deviceTimeZone">The IANA time zone of a device the file names that the tenant does not have yet.</param>
    Task<TestEmployeeLoadOutcome> LoadAsync(Stream csv, string deviceTimeZone, CancellationToken cancellationToken);
}
