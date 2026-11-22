namespace robeem.Tests.Attendance.Scenarios;

/// <summary>What one employee does on one day.</summary>
internal enum PlanKind
{
    /// <summary>Nothing is expected and nothing happens: a day off, or an hourly employee who does not come.</summary>
    Nothing,
    Perfect,
    Late,
    EarlyLeave,
    LateAndEarly,
    Overtime,
    NoBreak,
    LongBreak,
    LateBreak,
    EarlyBreak,
    ShortBreak,
    MissingCheckOut,
    MissingCheckIn,
    FirstIsCheckOut,
    LastIsCheckIn,
    TapMorning,
    TapEvening,
    BreakInTwice,
    BreakOutTwice,
    NoTypes,
    InOutOnly,

    /// <summary>A scheduled shift and no punch.</summary>
    NoShow,

    /// <summary>A session on a day with no shift.</summary>
    OffDayWork,
    HourlySession,
    HourlyOvernight,
    HourlySplit,
    HourlyMissingCheckOut,
}
