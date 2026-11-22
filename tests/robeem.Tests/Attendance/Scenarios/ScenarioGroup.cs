using robeem.Application.Attendance.Seeding;

namespace robeem.Tests.Attendance.Scenarios;

/// <summary>A time schedule (and one of its teams) as the scenario data expects it to behave, with the employees it has.</summary>
internal sealed class ScenarioGroup
{
    /// <summary>The first and last day of the scenario files.</summary>
    public static readonly DateTime FirstDay = new(2026, 4, 1);

    public static readonly DateTime LastDay = new(2026, 5, 30);

    private static readonly ScenarioShift DayShift = new("09:00", "17:00", "13:00", "14:00");

    private static readonly ScenarioShift CompressedDay = new("07:00", "18:00", "12:00", "13:00");

    private static readonly ScenarioShift Morning = new("06:00", "14:00");

    private static readonly ScenarioShift Afternoon = new("14:00", "22:00");

    private static readonly ScenarioShift Night = new("22:00", "06:00");

    // A Saturday: the first day of the Alternate Weekend Schedule's fortnight.
    private static readonly DateTime Anchor = new(2026, 1, 3);

    private readonly Func<DateTime, ScenarioShift?> _shiftOn;

    private ScenarioGroup(string scheduleTitle, string? teamTitle, bool onDemand, Func<DateTime, ScenarioShift?> shiftOn, ScenarioShift? typicalShift)
    {
        ScheduleTitle = scheduleTitle;
        TeamTitle = teamTitle;
        OnDemand = onDemand;
        _shiftOn = shiftOn;
        TypicalShift = typicalShift;
    }

    /// <summary>Gets every schedule the template seeder makes, with the teams that work, as they are meant to work.</summary>
    public static IReadOnlyList<ScenarioGroup> All { get; } =
    [
        Weekdays("Standard Week", DayShift, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday),
        Weekdays("Six-Day Workweek", DayShift, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday),
        Weekdays("Weekend Schedule", DayShift, DayOfWeek.Saturday, DayOfWeek.Sunday),
        Weekdays("Sunday-Thursday Workweek", DayShift, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday),
        new("Alternate Weekend Schedule", "Weekend A", false, d => FortnightDay(d) is 0 or 1 ? DayShift : null, DayShift),
        new("Alternate Weekend Schedule", "Weekend B", false, d => FortnightDay(d) is 7 or 8 ? DayShift : null, DayShift),
        Weekdays("Four-Day Workweek", CompressedDay, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday),
        new("Continuous Operations (7-Day)", "Morning Team", false, _ => Morning, Morning),
        new("Continuous Operations (7-Day)", "Afternoon Team", false, _ => Afternoon, Afternoon),
        new("Continuous Operations (7-Day)", "Night Team", false, _ => Night, Night),
        new("Custom Workweek", null, false, _ => null, null),
        new("On Demand (Hourly Staff)", null, true, _ => null, null),
    ];

    public string ScheduleTitle { get; }

    /// <summary>Gets the team's name; null for the schedule's team for everyone.</summary>
    public string? TeamTitle { get; }

    public bool OnDemand { get; }

    /// <summary>Gets the shift the group works, for deciding which cases fit it; null when it never works one.</summary>
    public ScenarioShift? TypicalShift { get; }

    public bool HasWorkingDays => TypicalShift is not null;

    public bool HasBreak => TypicalShift?.HasBreak == true;

    /// <summary>Gets the cases (employees) this group has.</summary>
    public IReadOnlyList<ScenarioCase> Cases => AttendanceScenarioCatalog.CasesFor(OnDemand, HasWorkingDays, HasBreak);

    /// <summary>Gets the employees whose files hold every kind of day of this group.</summary>
    public IReadOnlyList<ScenarioCase> CoverageCases => AttendanceScenarioCatalog.CoverageCases(Prefix);

    public string Prefix => AttendanceScenarioCatalog.Prefix(ScheduleTitle, TeamTitle);

    public ScenarioShift? ShiftOn(DateTime day) => _shiftOn(day.Date);

    public ScenarioEmployee EmployeeFor(ScenarioCase scenario) => AttendanceScenarioCatalog.Employee(ScheduleTitle, TeamTitle, scenario);

    private static ScenarioGroup Weekdays(string title, ScenarioShift shift, params DayOfWeek[] days) =>
        new(title, null, false, d => days.Contains(d.DayOfWeek) ? shift : null, shift);

    /// <summary>The day of the Alternate Weekend Schedule's 14-day pattern: 0 is the first Saturday of it.</summary>
    private static int FortnightDay(DateTime day) => (((int)(day.Date - Anchor).TotalDays % 14) + 14) % 14;
}
