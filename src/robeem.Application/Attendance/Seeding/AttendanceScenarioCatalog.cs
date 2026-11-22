using System.Text;

namespace robeem.Application.Attendance.Seeding;

/// <summary>
/// The people of the scenario data: for every time schedule (and each of its teams) one employee per case, named after the schedule, so
/// that every kind of schedule meets every kind of day. The punches of each are made by the test data generator in the tests project; this
/// is only who they are, so the seeder, the tests and the CSV files agree on the employee numbers.
/// </summary>
public static class AttendanceScenarioCatalog
{
    public static readonly IReadOnlyList<ScenarioCase> Cases =
    [
        new("PERFECT", "On time", "Checks in and out on time and takes the scheduled break: no exception, approved."),
        new("LATE", "Arrives late", "Checks in 11 to 45 minutes late and leaves on time: worked less than scheduled."),
        new("EARLY_OUT", "Leaves early", "Checks out 11 to 45 minutes early: worked less than scheduled."),
        new("LATE_EARLY", "Late and leaves early", "Both late and early out on the same day."),
        new("OVERTIME", "Works overtime", "Stays 15 to 90 minutes after the shift ends: overtime, approved unless pre-approval is required."),
        new("NO_BREAK", "Works through the break", "Punches in and out only: a break was scheduled and none was taken.", NeedsBreak: true),
        new("LONG_BREAK", "Long break", "The break runs 10 to 40 minutes over.", NeedsBreak: true),
        new("LATE_BREAK", "Late break", "The break starts 20 to 40 minutes after the scheduled start.", NeedsBreak: true),
        new("EARLY_BREAK", "Early break", "The break starts 20 to 40 minutes before the scheduled start.", NeedsBreak: true),
        new("SHORT_BREAK", "Short break", "A five-minute break-out and break-in (a toilet visit) on top of the normal day."),
        new("MISSING_OUT", "Forgets to check out", "A check-in and no check-out: a missing punch once the shift is over."),
        new("MISSING_IN", "Forgets to check in", "Break and check-out punches but no check-in: a missing punch."),
        new("FIRST_IS_OUT", "First punch is a check-out", "A check-out typed where the check-in belongs, at the start, then the real check-out."),
        new("LAST_IS_IN", "Last punch is a check-in", "The real check-in, then a check-in typed where the check-out belongs, at the end."),
        new("TAP_MORNING", "Taps twice in the morning", "Two check-ins a few minutes apart at the start of the day."),
        new("TAP_EVENING", "Taps twice in the evening", "Two check-outs a few minutes apart at the end of the day."),
        new("BREAK_IN_TWICE", "Break-in instead of break-out", "A break-in where the break-out belongs, then the real break-in.", NeedsBreak: true),
        new("BREAK_OUT_TWICE", "Break-out instead of break-in", "The real break-out, then a break-out where the break-in belongs.", NeedsBreak: true),
        new("NO_TYPES", "Punches without a type", "The clock sends no in/out type: the punches can only be read in order."),
        new("IN_OUT_ONLY", "In and out for the break", "A clock with no break key: the break is a check-out and a check-in.", NeedsBreak: true),
        new("ABSENT", "Absent", "No punch on any scheduled day: a no-show."),
        new("OFF_DAY", "Works on a day off", "Works the scheduled days, and punches in and out on days with no shift."),
        new("MIXED", "Mixed days", "Every day is one of the above, chosen at random."),
        new("H_PERFECT", "Works a session", "Paid by the hour: checks in and out, no shift to compare with.", Hourly: true),
        new("H_OVERNIGHT", "Works overnight", "Paid by the hour: a session from the evening past midnight, counted on the day of the check-in.", Hourly: true),
        new("H_SPLIT", "Works two sessions", "Paid by the hour: leaves and comes back the same day.", Hourly: true),
        new("H_MISSING_OUT", "Forgets to check out", "Paid by the hour: a check-in and no check-out.", Hourly: true),
        new("H_ABSENT", "Does not come", "Paid by the hour: no punch at all, and no exception for it.", Hourly: true),
        new("H_MIXED", "Mixed days", "Paid by the hour: every day is one of the above, chosen at random.", Hourly: true),
    ];

    /// <summary>
    /// How many employees a schedule's team needs so that their files together hold every kind of day: a file has a kind of day for each working
    /// day of the period, and a schedule that works few days (a weekend, every other weekend) cannot fit all of them in one. Any other has one.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> CoverageEmployees = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["WS"] = 2,
        ["AWS-WA"] = 3,
        ["AWS-WB"] = 3,
    };

    /// <summary>
    /// The cases that fit a kind of schedule: hourly ones for an on-demand schedule only; for a schedule with no working day, only
    /// working on a day off; the break cases only on a shift with a break.
    /// </summary>
    public static IReadOnlyList<ScenarioCase> CasesFor(bool onDemand, bool hasWorkingDays, bool hasBreak) =>
        Cases
            .Where(c => c.Hourly == onDemand)
            .Where(c => onDemand || hasWorkingDays || c.Code == "OFF_DAY")
            .Where(c => !c.NeedsBreak || hasBreak)
            .ToList();

    /// <summary>
    /// The employees whose files hold every kind of day for a schedule and team (<paramref name="prefix"/> as <see cref="Prefix"/> makes it):
    /// one, or a few when the schedule works too few days for one file to hold them all. Each day of the period, an employee has the next kind
    /// of day of theirs; together the employees of a team have every kind that fits the team's shift.
    /// </summary>
    public static IReadOnlyList<ScenarioCase> CoverageCases(string prefix)
    {
        var count = CoverageEmployees.GetValueOrDefault(prefix, 1);
        var cases = new List<ScenarioCase>();
        for (var i = 1; i <= count; i++)
        {
            var code = count == 1 ? "ALL" : $"ALL{i}";
            var title = count == 1 ? "Every case" : $"Every case ({i} of {count})";
            var description = count == 1
                ? "Every kind of day in turn, over the whole period."
                : $"Every {count}th kind of day, starting at the {i}th, in turn over the whole period; the {count} employees together have every kind.";
            cases.Add(new ScenarioCase(code, title, description));
        }

        return cases;
    }

    /// <summary>The employee number prefix of a schedule and team: the initials of the schedule's words, and of the team's when it is not the default one.</summary>
    public static string Prefix(string scheduleTitle, string? teamTitle) =>
        teamTitle is null ? Initials(scheduleTitle) : $"{Initials(scheduleTitle)}-{Initials(teamTitle)}";

    public static ScenarioEmployee Employee(string scheduleTitle, string? teamTitle, ScenarioCase scenario) =>
        new(
            $"{Prefix(scheduleTitle, teamTitle)}-{scenario.Code}",
            teamTitle is null ? scheduleTitle : $"{scheduleTitle} / {teamTitle}",
            scenario.Title,
            scheduleTitle,
            scheduleTitle,
            teamTitle,
            scenario);

    private static string Initials(string text)
    {
        var initials = new StringBuilder();
        var startOfWord = true;
        foreach (var c in text)
        {
            if (!char.IsLetterOrDigit(c))
            {
                startOfWord = true;
                continue;
            }

            if (startOfWord)
            {
                initials.Append(char.ToUpperInvariant(c));
            }

            startOfWord = false;
        }

        return initials.ToString();
    }
}
