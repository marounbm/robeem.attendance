namespace robeem.Contracts.Attendance;

/// <summary>
/// The employee's corrections of one of their days, sent together with one reason and decided together by the approvers.
/// The employee is the authenticated user.
/// </summary>
public sealed class DayCorrectionRequest
{
    public const string Change = "Change";
    public const string Ignore = "Ignore";
    public const string Add = "Add";

    /// <summary>Gets the attendance date being corrected.</summary>
    public DateTime Date { get; init; }

    /// <summary>Gets why the day is corrected. Required.</summary>
    public string Justification { get; init; } = string.Empty;

    public IReadOnlyList<DayCorrectionEdit> Edits { get; init; } = Array.Empty<DayCorrectionEdit>();
}
