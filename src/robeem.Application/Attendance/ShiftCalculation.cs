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

    /// <summary>Gets a value indicating whether every expected punch is present, in a valid sequence, with no punch left unlabeled.</summary>
    public bool IsComplete { get; init; }

    public IReadOnlyList<int> SourceIds { get; init; } = [];

    /// <summary>Gets the punches the calculation worked from, in time order: typed (inferred ones included), without repeats inside the duplicate window. Exception detection reads the same list.</summary>
    public IReadOnlyList<AttendancePunch> Punches { get; init; } = [];
}
