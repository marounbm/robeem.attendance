namespace robeem.Application.Attendance.TestEmployees;

/// <summary>
/// Deletes the employees named in a CSV file, with everything of theirs: their attendance data, raw punches included, and the employee records. For
/// test employees loaded by <see cref="ITestEmployeeLoader"/>; the file is the same one, and only its employee numbers are read. It is the second
/// time raw attendance is deleted, after <c>IAttendanceDataResetService</c> (CLAUDE.md §1.2).
/// </summary>
public interface ITestEmployeeRemover
{
    /// <summary>What a deletion of the file's employees would delete. Nothing is deleted.</summary>
    Task<TestEmployeeDeleteOutcome> PreviewAsync(Stream csv, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes them, in one transaction, with an audit entry that is kept. Refused when the host doesn't allow it, when <paramref name="confirmation"/>
    /// is not the tenant id as typed, when the file is wrong, or when someone who is not in the file still has one of them as manager.
    /// </summary>
    Task<TestEmployeeDeleteOutcome> DeleteAsync(Stream csv, string confirmation, CancellationToken cancellationToken);
}
