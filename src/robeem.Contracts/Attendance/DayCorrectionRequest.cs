namespace robeem.Contracts.Attendance;

/// <summary>
/// The corrections of one day of an employee, sent together with one reason and decided together by the approvers. The person who sends
/// them is always the authenticated user; the day is theirs unless <see cref="EmployeeId"/> names another employee, which only that
/// employee's manager or HR may do.
/// </summary>
public sealed class DayCorrectionRequest
{
    public const string Change = "Change";
    public const string Ignore = "Ignore";
    public const string Add = "Add";

    /// <summary>
    /// Gets the employee whose day is corrected; null for the authenticated user's own day. A manager or HR names an employee to correct
    /// their day on their behalf; for anyone else the Api refuses it.
    /// </summary>
    public int? EmployeeId { get; init; }

    /// <summary>Gets the attendance date being corrected.</summary>
    public DateTime Date { get; init; }

    /// <summary>Gets why the day is corrected. Optional.</summary>
    public string Justification { get; init; } = string.Empty;

    public IReadOnlyList<DayCorrectionEdit> Edits { get; init; } = Array.Empty<DayCorrectionEdit>();
}
