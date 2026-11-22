using robeem.Application.Attendance.Seeding;
using robeem.DataModel;

namespace robeem.Tests.Attendance.Scenarios;

/// <summary>
/// Makes each employee's days: which of the kinds of day a case has, and the punches of it, with times that vary from day to day.
/// The variation is random but fixed by the employee and the date, so the files and the tests are the same every time.
/// </summary>
internal static class ScenarioPlanner
{
    private static readonly PlanKind[] ScheduledMix =
    [
        PlanKind.Perfect, PlanKind.Perfect, PlanKind.Perfect, PlanKind.Late, PlanKind.EarlyLeave, PlanKind.LateAndEarly, PlanKind.Overtime,
        PlanKind.NoBreak, PlanKind.LongBreak, PlanKind.LateBreak, PlanKind.EarlyBreak, PlanKind.ShortBreak, PlanKind.MissingCheckOut,
        PlanKind.MissingCheckIn, PlanKind.FirstIsCheckOut, PlanKind.LastIsCheckIn, PlanKind.TapMorning, PlanKind.TapEvening,
        PlanKind.BreakInTwice, PlanKind.BreakOutTwice, PlanKind.NoTypes, PlanKind.InOutOnly, PlanKind.NoShow,
    ];

    private static readonly PlanKind[] HourlyMix =
        [PlanKind.HourlySession, PlanKind.HourlyOvernight, PlanKind.HourlySplit, PlanKind.HourlyMissingCheckOut, PlanKind.Nothing];

    /// <summary>The kinds that only make sense on a shift with a break.</summary>
    private static readonly PlanKind[] NeedsBreak =
        [PlanKind.NoBreak, PlanKind.LongBreak, PlanKind.LateBreak, PlanKind.EarlyBreak, PlanKind.BreakInTwice, PlanKind.BreakOutTwice, PlanKind.InOutOnly];

    /// <summary>The kinds of day of a coverage employee (<see cref="AttendanceScenarioCatalog.CoverageCases"/>) that mean a person at work or away.</summary>
    private static readonly IReadOnlyDictionary<PlanKind, string> Codes = new Dictionary<PlanKind, string>
    {
        [PlanKind.Perfect] = "PERFECT",
        [PlanKind.Late] = "LATE",
        [PlanKind.EarlyLeave] = "EARLY_OUT",
        [PlanKind.LateAndEarly] = "LATE_EARLY",
        [PlanKind.Overtime] = "OVERTIME",
        [PlanKind.NoBreak] = "NO_BREAK",
        [PlanKind.LongBreak] = "LONG_BREAK",
        [PlanKind.LateBreak] = "LATE_BREAK",
        [PlanKind.EarlyBreak] = "EARLY_BREAK",
        [PlanKind.ShortBreak] = "SHORT_BREAK",
        [PlanKind.MissingCheckOut] = "MISSING_OUT",
        [PlanKind.MissingCheckIn] = "MISSING_IN",
        [PlanKind.FirstIsCheckOut] = "FIRST_IS_OUT",
        [PlanKind.LastIsCheckIn] = "LAST_IS_IN",
        [PlanKind.TapMorning] = "TAP_MORNING",
        [PlanKind.TapEvening] = "TAP_EVENING",
        [PlanKind.BreakInTwice] = "BREAK_IN_TWICE",
        [PlanKind.BreakOutTwice] = "BREAK_OUT_TWICE",
        [PlanKind.NoTypes] = "NO_TYPES",
        [PlanKind.InOutOnly] = "IN_OUT_ONLY",
        [PlanKind.NoShow] = "ABSENT",
        [PlanKind.OffDayWork] = "OFF_DAY",
        [PlanKind.HourlySession] = "H_PERFECT",
        [PlanKind.HourlyOvernight] = "H_OVERNIGHT",
        [PlanKind.HourlySplit] = "H_SPLIT",
        [PlanKind.HourlyMissingCheckOut] = "H_MISSING_OUT",
        [PlanKind.Nothing] = "H_ABSENT",
    };

    /// <summary>The code (of <see cref="AttendanceScenarioCatalog.Cases"/>) of a kind of day.</summary>
    public static string CodeOf(PlanKind kind) => Codes[kind];

    /// <summary>
    /// The kinds of day a group works through: every kind that fits its shift (those about the break only on a shift with a break), or the hourly ones;
    /// a schedule that works no day has only work on a day off.
    /// </summary>
    public static IReadOnlyList<PlanKind> CoverageKinds(ScenarioGroup group)
    {
        if (group.OnDemand)
        {
            return HourlyMix;
        }

        return group.HasWorkingDays
            ? ScheduledMix.Distinct().Where(k => group.HasBreak || !NeedsBreak.Contains(k)).ToList()
            : [PlanKind.OffDayWork];
    }

    /// <summary>
    /// An employee's days over the period: a coverage employee (code <c>ALL</c>, or <c>ALL1</c>, <c>ALL2</c>... when a team needs several) has, on each
    /// day the schedule works, the next of its kinds of day in turn, and, on a day it does not, sometimes works anyway; any other case is its own
    /// kind of day, every working day.
    /// </summary>
    public static IReadOnlyList<DayPlan> PlanAll(ScenarioGroup group, ScenarioCase scenario, DateTime first, DateTime last)
    {
        var days = Enumerable.Range(0, (last.Date - first.Date).Days + 1).Select(i => first.Date.AddDays(i)).ToList();
        if (!scenario.Code.StartsWith("ALL", StringComparison.Ordinal))
        {
            return days.Select(day => Plan(group, scenario, day)).ToList();
        }

        var employees = AttendanceScenarioCatalog.CoverageCases(group.Prefix).Count;
        var index = scenario.Code == "ALL" ? 0 : int.Parse(scenario.Code[3..], global::System.Globalization.CultureInfo.InvariantCulture) - 1;
        var kinds = CoverageKinds(group).Where((_, i) => i % employees == index).ToList();
        var number = group.EmployeeFor(scenario).Number;
        var plans = new List<DayPlan>();
        var worked = 0;
        foreach (var day in days)
        {
            var random = new Random(Seed($"{number}|{day:yyyyMMdd}"));
            var shift = group.ShiftOn(day);
            var kind = group.OnDemand || shift is not null
                ? kinds[worked++ % kinds.Count]
                : (random.Next(group.HasWorkingDays ? 4 : 2) == 0 ? PlanKind.OffDayWork : PlanKind.Nothing);
            plans.Add(new DayPlan(day, shift, kind, Punches(kind, day, shift, random)));
        }

        return plans;
    }

    public static DayPlan Plan(ScenarioGroup group, ScenarioCase scenario, DateTime day)
    {
        var number = group.EmployeeFor(scenario).Number;
        var random = new Random(Seed($"{number}|{day:yyyyMMdd}"));
        var shift = group.ShiftOn(day);

        var kind = group.OnDemand ? HourlyKind(scenario, random) : ScheduledKind(scenario, shift, random);
        if (shift is { HasBreak: false } && NeedsBreak.Contains(kind))
        {
            kind = PlanKind.Perfect;
        }

        return new DayPlan(day.Date, shift, kind, Punches(kind, day.Date, shift, random));
    }

    private static PlanKind HourlyKind(ScenarioCase scenario, Random random)
    {
        var comesIn = random.Next(10) < 7;
        return scenario.Code switch
        {
            "H_PERFECT" => comesIn ? PlanKind.HourlySession : PlanKind.Nothing,
            "H_OVERNIGHT" => comesIn ? PlanKind.HourlyOvernight : PlanKind.Nothing,
            "H_SPLIT" => comesIn ? PlanKind.HourlySplit : PlanKind.Nothing,
            "H_MISSING_OUT" => comesIn ? PlanKind.HourlyMissingCheckOut : PlanKind.Nothing,
            "H_MIXED" => HourlyMix[random.Next(HourlyMix.Length)],
            _ => PlanKind.Nothing,
        };
    }

    private static PlanKind ScheduledKind(ScenarioCase scenario, ScenarioShift? shift, Random random)
    {
        if (shift is null)
        {
            // A day with no shift: nobody is expected, but some people punch anyway.
            var comes = scenario.Code switch
            {
                "OFF_DAY" => random.Next(2) == 0,
                "MIXED" => random.Next(4) == 0,
                _ => false,
            };
            return comes ? PlanKind.OffDayWork : PlanKind.Nothing;
        }

        return scenario.Code switch
        {
            "PERFECT" or "OFF_DAY" => PlanKind.Perfect,
            "LATE" => PlanKind.Late,
            "EARLY_OUT" => PlanKind.EarlyLeave,
            "LATE_EARLY" => PlanKind.LateAndEarly,
            "OVERTIME" => PlanKind.Overtime,
            "NO_BREAK" => PlanKind.NoBreak,
            "LONG_BREAK" => PlanKind.LongBreak,
            "LATE_BREAK" => PlanKind.LateBreak,
            "EARLY_BREAK" => PlanKind.EarlyBreak,
            "SHORT_BREAK" => PlanKind.ShortBreak,
            "MISSING_OUT" => PlanKind.MissingCheckOut,
            "MISSING_IN" => PlanKind.MissingCheckIn,
            "FIRST_IS_OUT" => PlanKind.FirstIsCheckOut,
            "LAST_IS_IN" => PlanKind.LastIsCheckIn,
            "TAP_MORNING" => PlanKind.TapMorning,
            "TAP_EVENING" => PlanKind.TapEvening,
            "BREAK_IN_TWICE" => PlanKind.BreakInTwice,
            "BREAK_OUT_TWICE" => PlanKind.BreakOutTwice,
            "NO_TYPES" => PlanKind.NoTypes,
            "IN_OUT_ONLY" => PlanKind.InOutOnly,
            "ABSENT" => PlanKind.NoShow,
            "MIXED" => ScheduledMix[random.Next(ScheduledMix.Length)],
            _ => throw new InvalidOperationException($"No plan for the case {scenario.Code}."),
        };
    }

    private static IReadOnlyList<PlannedPunch> Punches(PlanKind kind, DateTime day, ScenarioShift? shift, Random random)
    {
        var punches = new List<PlannedPunch>();
        void Add(NormalizedEventType type, DateTime at) => punches.Add(new PlannedPunch(type, at));

        if (kind is PlanKind.Nothing or PlanKind.NoShow)
        {
            return punches;
        }

        switch (kind)
        {
            case PlanKind.OffDayWork:
                var arrives = day.AddHours(10).AddMinutes(random.Next(0, 60));
                Add(NormalizedEventType.CheckIn, arrives);
                Add(NormalizedEventType.CheckOut, arrives.AddHours(4).AddMinutes(random.Next(0, 30)));
                return punches;
            case PlanKind.HourlySession:
                // Starts after 08:00, so the night before's overnight session (which ends by 07:30) is over.
                var begins = day.AddHours(8).AddMinutes(random.Next(0, 180));
                Add(NormalizedEventType.CheckIn, begins);
                Add(NormalizedEventType.CheckOut, begins.AddHours(4).AddMinutes(random.Next(0, 360)));
                return punches;
            case PlanKind.HourlyOvernight:
                var evening = day.AddHours(20).AddMinutes(random.Next(0, 180));
                Add(NormalizedEventType.CheckIn, evening);
                Add(NormalizedEventType.CheckOut, evening.AddHours(8).AddMinutes(random.Next(0, 30)));
                return punches;
            case PlanKind.HourlySplit:
                var morning = day.AddHours(8).AddMinutes(random.Next(0, 90));
                Add(NormalizedEventType.CheckIn, morning);
                Add(NormalizedEventType.CheckOut, morning.AddHours(4).AddMinutes(random.Next(0, 30)));
                var afternoon = day.AddHours(14).AddMinutes(random.Next(0, 60));
                Add(NormalizedEventType.CheckIn, afternoon);
                Add(NormalizedEventType.CheckOut, afternoon.AddHours(3).AddMinutes(random.Next(0, 90)));
                return punches;
            case PlanKind.HourlyMissingCheckOut:
                Add(NormalizedEventType.CheckIn, day.AddHours(8).AddMinutes(random.Next(0, 180)));
                return punches;
        }

        var start = shift!.StartsOn(day);
        var end = shift.EndsOn(day);
        var breakStart = shift.HasBreak ? shift.BreakStartsOn(day) : default;
        var breakEnd = shift.HasBreak ? shift.BreakEndsOn(day) : default;
        var drift = random.Next(0, 4);

        // The scheduled break, exactly as scheduled; the kinds that change it do so below.
        void Break(DateTime from, DateTime to)
        {
            if (shift.HasBreak)
            {
                Add(NormalizedEventType.BreakOut, from);
                Add(NormalizedEventType.BreakIn, to);
            }
        }

        switch (kind)
        {
            case PlanKind.Perfect:
                Add(NormalizedEventType.CheckIn, start.AddMinutes(drift));
                Break(breakStart, breakEnd);
                Add(NormalizedEventType.CheckOut, end.AddMinutes(drift));
                break;
            case PlanKind.Late:
                Add(NormalizedEventType.CheckIn, start.AddMinutes(random.Next(11, 46)));
                Break(breakStart, breakEnd);
                Add(NormalizedEventType.CheckOut, end);
                break;
            case PlanKind.EarlyLeave:
                Add(NormalizedEventType.CheckIn, start);
                Break(breakStart, breakEnd);
                Add(NormalizedEventType.CheckOut, end.AddMinutes(-random.Next(11, 46)));
                break;
            case PlanKind.LateAndEarly:
                Add(NormalizedEventType.CheckIn, start.AddMinutes(random.Next(11, 31)));
                Break(breakStart, breakEnd);
                Add(NormalizedEventType.CheckOut, end.AddMinutes(-random.Next(11, 31)));
                break;
            case PlanKind.Overtime:
                Add(NormalizedEventType.CheckIn, start);
                Break(breakStart, breakEnd);
                Add(NormalizedEventType.CheckOut, end.AddMinutes(random.Next(15, 91)));
                break;
            case PlanKind.NoBreak:
                Add(NormalizedEventType.CheckIn, start);
                Add(NormalizedEventType.CheckOut, end);
                break;
            case PlanKind.LongBreak:
                Add(NormalizedEventType.CheckIn, start);
                Break(breakStart, breakEnd.AddMinutes(random.Next(10, 41)));
                Add(NormalizedEventType.CheckOut, end);
                break;
            case PlanKind.LateBreak:
                var later = random.Next(20, 41);
                Add(NormalizedEventType.CheckIn, start);
                Break(breakStart.AddMinutes(later), breakEnd.AddMinutes(later));
                Add(NormalizedEventType.CheckOut, end);
                break;
            case PlanKind.EarlyBreak:
                var earlier = random.Next(20, 41);
                Add(NormalizedEventType.CheckIn, start);
                Break(breakStart.AddMinutes(-earlier), breakEnd.AddMinutes(-earlier));
                Add(NormalizedEventType.CheckOut, end);
                break;
            case PlanKind.ShortBreak:
                var visit = shift.HasBreak ? breakEnd.AddMinutes(60) : start.AddHours(4);
                Add(NormalizedEventType.CheckIn, start);
                Break(breakStart, breakEnd);
                Add(NormalizedEventType.BreakOut, visit);
                Add(NormalizedEventType.BreakIn, visit.AddMinutes(5));
                Add(NormalizedEventType.CheckOut, end);
                break;
            case PlanKind.MissingCheckOut:
                Add(NormalizedEventType.CheckIn, start.AddMinutes(drift));
                Break(breakStart, breakEnd);
                break;
            case PlanKind.MissingCheckIn:
                Break(breakStart, breakEnd);
                Add(NormalizedEventType.CheckOut, end.AddMinutes(drift));
                break;
            case PlanKind.FirstIsCheckOut:
                Add(NormalizedEventType.CheckOut, start.AddMinutes(random.Next(1, 4)));
                Add(NormalizedEventType.CheckOut, end);
                break;
            case PlanKind.LastIsCheckIn:
                Add(NormalizedEventType.CheckIn, start);
                Add(NormalizedEventType.CheckIn, end.AddMinutes(-random.Next(1, 4)));
                break;
            case PlanKind.TapMorning:
                var firstTap = start.AddMinutes(drift);
                Add(NormalizedEventType.CheckIn, firstTap);
                Add(NormalizedEventType.CheckIn, firstTap.AddMinutes(random.Next(3, 9)));
                Break(breakStart, breakEnd);
                Add(NormalizedEventType.CheckOut, end);
                break;
            case PlanKind.TapEvening:
                var lastTap = end.AddMinutes(drift);
                Add(NormalizedEventType.CheckIn, start);
                Break(breakStart, breakEnd);
                Add(NormalizedEventType.CheckOut, lastTap);
                Add(NormalizedEventType.CheckOut, lastTap.AddMinutes(random.Next(3, 9)));
                break;
            case PlanKind.BreakInTwice:
                Add(NormalizedEventType.CheckIn, start);
                Add(NormalizedEventType.BreakIn, breakStart);
                Add(NormalizedEventType.BreakIn, breakEnd);
                Add(NormalizedEventType.CheckOut, end);
                break;
            case PlanKind.BreakOutTwice:
                Add(NormalizedEventType.CheckIn, start);
                Add(NormalizedEventType.BreakOut, breakStart);
                Add(NormalizedEventType.BreakOut, breakEnd);
                Add(NormalizedEventType.CheckOut, end);
                break;
            case PlanKind.NoTypes:
                Add(NormalizedEventType.Unknown, start);
                if (shift.HasBreak)
                {
                    Add(NormalizedEventType.Unknown, breakStart);
                    Add(NormalizedEventType.Unknown, breakEnd);
                }

                Add(NormalizedEventType.Unknown, end);
                break;
            case PlanKind.InOutOnly:
                Add(NormalizedEventType.CheckIn, start);
                Add(NormalizedEventType.CheckOut, breakStart);
                Add(NormalizedEventType.CheckIn, breakEnd);
                Add(NormalizedEventType.CheckOut, end);
                break;
            default:
                throw new InvalidOperationException($"No punches for {kind}.");
        }

        return punches;
    }

    /// <summary>A number from text that is the same on every run and machine, unlike <c>string.GetHashCode</c>.</summary>
    private static int Seed(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return (int)(hash & 0x7FFFFFFF);
    }
}
