namespace robeem.Application.Attendance;

/// <summary>Builds the facts of a finding about a shift, in one place for everything that asks the rules.</summary>
internal static class ShiftFacts
{
    /// <summary>The facts of a finding on <paramref name="shift"/> (null for work outside a shift) that goes <paramref name="minutesOver"/> past what is allowed.</summary>
    public static RuleFacts For(ResolvedShift? shift, int minutesOver, int excusedMinutes) => new()
    {
        MinutesOver = minutesOver,
        ShiftHasBreak = shift?.BreakTime == true,
        ShiftIsOvernight = shift is not null && shift.EndsAt.Date > shift.StartsAt.Date,
        ExcusedMinutes = excusedMinutes,
    };
}
