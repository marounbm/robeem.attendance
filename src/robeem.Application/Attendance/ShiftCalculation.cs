using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>The outcome of <see cref="ShiftAttendanceCalculator.Calculate"/> for one shift.</summary>
internal sealed record ShiftCalculation
{
    public AttendancePunch? CheckIn { get; init; }

    public AttendancePunch? CheckOut { get; init; }

    public AttendancePunch? BreakOut { get; init; }

    public AttendancePunch? BreakIn { get; init; }

    public TimeSpan WorkingHours { get; init; }

    public TimeSpan BreakDuration { get; init; }

    public int LateMinutes { get; init; }

    public int EarlyDepartureMinutes { get; init; }

    public int OvertimeMinutes { get; init; }

    /// <summary>Gets what was found about the break: long, late, early, or none taken. Always computed; the policy decides what is raised.</summary>
    public BreakFindings BreakFindings { get; init; }

    /// <summary>Gets how many minutes the breaks together lasted beyond the scheduled break; 0 when they did not.</summary>
    public int BreakExcessMinutes { get; init; }

    /// <summary>Gets how many minutes the first break started away from schedule (positive is late, negative is early); 0 without a break.</summary>
    public int BreakStartOffsetMinutes { get; init; }

    /// <summary>Gets how many breaks were left out for being shorter than the policy's minimum.</summary>
    public int IgnoredShortBreaks { get; init; }

    /// <summary>Gets a value indicating whether every expected punch is present, in a valid sequence, with no punch left unlabeled.</summary>
    public bool IsComplete { get; init; }

    public IReadOnlyList<int> SourceIds { get; init; } = [];

    /// <summary>Gets the applied corrections that supplied a punch.</summary>
    public IReadOnlyList<int> CorrectionIds { get; init; } = [];

    /// <summary>Gets the punches the calculation worked from, in time order: typed (inferred ones included), without repeats inside the duplicate window. Exception detection reads the same list.</summary>
    public IReadOnlyList<AttendancePunch> Punches { get; init; } = [];
}
