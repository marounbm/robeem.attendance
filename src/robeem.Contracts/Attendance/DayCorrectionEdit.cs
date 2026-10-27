namespace robeem.Contracts.Attendance;

/// <summary>One edit of a day: change a punch's type, ignore a punch, or add a punch that is missing.</summary>
public sealed class DayCorrectionEdit
{
    /// <summary>Gets what to do: <see cref="DayCorrectionRequest.Change"/>, <see cref="DayCorrectionRequest.Ignore"/> or <see cref="DayCorrectionRequest.Add"/>.</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>Gets the punch to change or ignore (<see cref="DayPunchItem.TransactionId"/>). Not used to add one.</summary>
    public int? TransactionId { get; init; }

    /// <summary>
    /// Gets the earlier correction that added the punch to delete (<see cref="DayPunchItem.CorrectionId"/>). Only used with
    /// <see cref="DayCorrectionRequest.Ignore"/>, in place of <see cref="TransactionId"/>, for a punch a correction added.
    /// </summary>
    public int? CorrectionId { get; init; }

    /// <summary>Gets the type to give the punch, or the type of the punch to add: CheckIn, CheckOut, BreakOut or BreakIn. Not used to ignore one.</summary>
    public string? EventType { get; init; }

    /// <summary>Gets when the added punch happened, as local wall-clock time. Only used to add a punch.</summary>
    public DateTime? At { get; init; }
}
