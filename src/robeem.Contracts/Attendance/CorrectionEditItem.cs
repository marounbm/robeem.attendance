namespace robeem.Contracts.Attendance;

/// <summary>One edit of a correction request: a punch added, a punch given another type, or a punch ignored.</summary>
public sealed class CorrectionEditItem
{
    /// <summary>Gets what it does: SupplyPunch (adds a punch), ChangePunchType or IgnorePunch.</summary>
    public string Kind { get; init; } = "SupplyPunch";

    /// <summary>Gets the type the punch had, for a change of type or an ignored punch; null for an added punch.</summary>
    public string? OriginalEventType { get; init; }

    /// <summary>Gets the punch added, or the type the existing punch becomes. For an ignored punch, the type it had.</summary>
    public string CorrectedEventType { get; init; } = string.Empty;

    /// <summary>Gets when the punch happened (an added punch), or the time of the existing punch that is changed or ignored.</summary>
    public DateTime At { get; init; }
}
