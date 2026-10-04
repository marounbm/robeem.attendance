namespace robeem.Application.Attendance;

/// <summary>
/// What an employee is expected to work on one date. Both <see cref="ShiftResolutionOutcome.DayOff"/>
/// and <see cref="ShiftResolutionOutcome.NoScheduleConfigured"/> are expected outcomes, not errors.
/// </summary>
/// <param name="Date">The date resolved (date only).</param>
/// <param name="Outcome">Whether the employee is scheduled, off, or has no schedule.</param>
/// <param name="CycleDay">The schedule's cycle day for <paramref name="Date"/>; null without a schedule.</param>
/// <param name="Shifts">The shifts, ordered by start time; empty unless <see cref="ShiftResolutionOutcome.Scheduled"/>.
/// A day may hold several (a split shift): a team can have several lines on one cycle day.</param>
public sealed record ShiftResolution(DateTime Date, ShiftResolutionOutcome Outcome, int? CycleDay, IReadOnlyList<ResolvedShift> Shifts)
{
    public bool IsScheduled => Outcome == ShiftResolutionOutcome.Scheduled;

    public static ShiftResolution NoScheduleConfigured(DateTime date) => new(date, ShiftResolutionOutcome.NoScheduleConfigured, null, []);

    public static ShiftResolution DayOff(DateTime date, int cycleDay) => new(date, ShiftResolutionOutcome.DayOff, cycleDay, []);

    public static ShiftResolution Scheduled(DateTime date, int cycleDay, IReadOnlyList<ResolvedShift> shifts) =>
        new(date, ShiftResolutionOutcome.Scheduled, cycleDay, shifts);
}
