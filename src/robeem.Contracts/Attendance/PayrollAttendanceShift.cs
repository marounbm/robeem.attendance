namespace robeem.Contracts.Attendance;

/// <summary>One approved shift of a <see cref="PayrollAttendanceDay"/>. Times are local wall-clock times.</summary>
public sealed record PayrollAttendanceShift(
    int RecordId,
    int? TimeScheduleShiftId,
    DateTime? ScheduledStart,
    DateTime? ScheduledEnd,
    DateTime? ActualCheckIn,
    DateTime? ActualCheckOut,
    TimeSpan WorkingHours,
    TimeSpan BreakDuration,
    int LateMinutes,
    int EarlyDepartureMinutes,
    int OvertimeMinutes);
