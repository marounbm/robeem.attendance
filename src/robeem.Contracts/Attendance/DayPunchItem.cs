namespace robeem.Contracts.Attendance;

/// <summary>One punch of the day, as the employee sees it on the Correct page, and what they can do with it.</summary>
public sealed class DayPunchItem
{
    /// <summary>Gets the normalized punch it is; null for a punch an applied correction added.</summary>
    public int? TransactionId { get; init; }

    /// <summary>Gets what the punch counts as: CheckIn, CheckOut, BreakOut, BreakIn, or Other for a tap that is not attendance. After a change of type, the new type.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Gets the type the device gave the punch, when a correction changed it; null otherwise.</summary>
    public string? OriginalType { get; init; }

    /// <summary>Gets when the punch happened, as local wall-clock time.</summary>
    public DateTime At { get; init; }

    /// <summary>Gets where it comes from: Device, or Corrected for a punch that a correction added.</summary>
    public string Source { get; init; } = "Device";

    /// <summary>
    /// Gets how it counts: Counted; Changed (a correction gave it another type); Added (a correction added it); Ignored (a correction left it out);
    /// Duplicate (a repeat of another punch, already left out); NotCounted (the device is not recognized, or the punch was replaced).
    /// </summary>
    public string State { get; init; } = "Counted";

    /// <summary>Gets a value indicating whether the employee can change or ignore it now. Only a punch that counts can be edited.</summary>
    public bool Editable { get; init; }

    /// <summary>Gets what is wrong with it, when it is the punch an open exception of the day points at; null otherwise.</summary>
    public string? Problem { get; init; }

    /// <summary>Gets the types it can be changed to so that its problem goes away; empty when it has no problem, or no other type settles it.</summary>
    public IReadOnlyList<string> SuggestedTypes { get; init; } = Array.Empty<string>();
}
