using robeem.DataModel;

namespace robeem.Tests.Attendance.Scenarios;

/// <summary>
/// What each kind of day must come to under a set of rules, worked out here from the punches and the shift alone, in the plainest way:
/// read the day the way the rules in force allow, and if it is a clean in-and-out day, add up the hours and ask each rule what it
/// makes of them. It shares no code with the attendance engine, so the two agreeing means something.
/// </summary>
internal static class ScenarioOracle
{
    public static ExpectedDay Expect(DayPlan plan, OracleSettings settings)
    {
        var rules = settings.Rules;
        var punches = plan.Punches.ToList();

        switch (plan.Kind)
        {
            case PlanKind.Nothing:
                return new ExpectedDay(None(), false, false, null, null);
            case PlanKind.NoShow:
                return new ExpectedDay(rules.NoShowRaises ? Set(AttendanceExceptionType.NoShow) : None(), true, true, null, null);
            case PlanKind.MissingCheckOut:
            case PlanKind.MissingCheckIn:
            case PlanKind.HourlyMissingCheckOut:
                return Broken(AttendanceExceptionType.MissingPunch);

            // A check-out typed where the check-in belongs: without the rule the day has no check-in and a check-out after a check-out.
            case PlanKind.FirstIsCheckOut:
                return rules.LoneCheckOutIsCheckIn
                    ? Clean(plan, [Typed(punches[0], NormalizedEventType.CheckIn), punches[1]], settings)
                    : Broken(AttendanceExceptionType.InvalidSequence, AttendanceExceptionType.MissingPunch);
            case PlanKind.LastIsCheckIn:
                return rules.LoneCheckInIsCheckOut
                    ? Clean(plan, [punches[0], Typed(punches[1], NormalizedEventType.CheckOut)], settings)
                    : Broken(AttendanceExceptionType.InvalidSequence, AttendanceExceptionType.MissingPunch);

            // One punch said twice: the rule keeps the first and leaves the second out.
            case PlanKind.TapMorning:
                return rules.MorningDuplicateIsOne ? Clean(plan, punches.Where((_, i) => i != 1).ToList(), settings) : Broken(AttendanceExceptionType.InvalidSequence);
            case PlanKind.TapEvening:
                return rules.EveningDuplicateIsOne ? Clean(plan, punches.Take(punches.Count - 1).ToList(), settings) : Broken(AttendanceExceptionType.InvalidSequence);

            // A break punch of the wrong type next to where the break is scheduled.
            case PlanKind.BreakInTwice:
                return rules.BreakInIsBreakOut
                    ? Clean(plan, [punches[0], Typed(punches[1], NormalizedEventType.BreakOut), punches[2], punches[3]], settings)
                    : Broken(AttendanceExceptionType.MissingPunch);
            case PlanKind.BreakOutTwice:
                return rules.BreakOutIsBreakIn
                    ? Clean(plan, [punches[0], punches[1], Typed(punches[2], NormalizedEventType.BreakIn), punches[3]], settings)
                    : Broken(AttendanceExceptionType.InvalidSequence, AttendanceExceptionType.MissingPunch);

            // A clock that sends no types: read in order when the policy says so. Otherwise normalization refuses every one of the punches (the
            // raw rows are kept, marked failed), so the day has no punch to calculate and is a no-show.
            case PlanKind.NoTypes:
                return settings.Sequential
                    ? Clean(plan, InOrder(punches), settings)
                    : new ExpectedDay(rules.NoShowRaises ? Set(AttendanceExceptionType.NoShow) : None(), true, true, null, null);

            // A clock with no break key: the gap near the scheduled break is read as the break when the rules say so.
            case PlanKind.InOutOnly:
                return rules.GapIsBreak
                    ? Clean(plan, [punches[0], Typed(punches[1], NormalizedEventType.BreakOut), Typed(punches[2], NormalizedEventType.BreakIn), punches[3]], settings)
                    : Clean(plan, punches, settings);

            // A five-minute break-out and break-in: counted as work when the rules say a short break is.
            case PlanKind.ShortBreak:
                return rules.ShortBreakIsWork ? Clean(plan, punches.Take(punches.Count - 3).Append(punches[^1]).ToList(), settings) : Clean(plan, punches, settings);

            default:
                return Clean(plan, punches, settings);
        }
    }

    private static HashSet<AttendanceExceptionType> None() => [];

    private static HashSet<AttendanceExceptionType> Set(params AttendanceExceptionType[] types) => [.. types];

    private static ExpectedDay Broken(params AttendanceExceptionType[] types) => new(Set(types), true, true, null, null);

    private static PlannedPunch Typed(PlannedPunch punch, NormalizedEventType type) => punch with { Type = type };

    private static List<PlannedPunch> InOrder(List<PlannedPunch> punches)
    {
        NormalizedEventType[] order = punches.Count == 4
            ? [NormalizedEventType.CheckIn, NormalizedEventType.BreakOut, NormalizedEventType.BreakIn, NormalizedEventType.CheckOut]
            : [NormalizedEventType.CheckIn, NormalizedEventType.CheckOut];
        return punches.Select((p, i) => Typed(p, order[i])).ToList();
    }

    /// <summary>A day whose punches read as a clean in and out, with or without a break: add it up and ask each rule.</summary>
    private static ExpectedDay Clean(DayPlan plan, IReadOnlyList<PlannedPunch> typed, OracleSettings settings)
    {
        var rules = settings.Rules;
        var shift = plan.Shift;
        var (working, onBreak) = Add(typed);
        var hourly = plan.Kind is PlanKind.HourlySession or PlanKind.HourlyOvernight or PlanKind.HourlySplit;

        // Work on a day with no shift: there is nothing to compare it with, so no rule has anything to say; a person has to look at it
        // (an employee paid by the hour needs nobody to).
        if (shift is null)
        {
            return new ExpectedDay(None(), !hourly, true, working, 0);
        }

        var exceptions = None();
        var hasBreakPunches = typed.Any(p => p.Type is NormalizedEventType.BreakOut or NormalizedEventType.BreakIn);
        if (shift.HasBreak && rules.NoBreakRaises && !hasBreakPunches)
        {
            // A shift with a break and none taken: the break punches are asked for, and until they are the day is not complete.
            exceptions.Add(AttendanceExceptionType.MissingPunch);
            return new ExpectedDay(exceptions, true, true, null, null);
        }

        var excess = 0;
        if (shift.HasBreak && hasBreakPunches)
        {
            var offset = (int)(typed.First(p => p.Type == NormalizedEventType.BreakOut).At - shift.BreakStartsOn(plan.Date)).TotalMinutes;
            excess = Math.Max(0, onBreak - shift.BreakMinutes);
            if (offset > 0 && rules.LateBreakRaises && offset > rules.LateBreakMinutesOver)
            {
                exceptions.Add(AttendanceExceptionType.LateBreak);
            }

            if (offset < 0 && rules.EarlyBreakRaises && -offset > rules.EarlyBreakMinutesOver)
            {
                exceptions.Add(AttendanceExceptionType.EarlyBreak);
            }

            if (excess > 0 && rules.LongBreakRaises && excess > rules.LongBreakMinutesOver)
            {
                exceptions.Add(AttendanceExceptionType.LongBreak);
            }
        }

        // The time over or under what the shift schedules; the minutes a break ran over are the break rules' to judge, so not counted twice.
        var extra = working - shift.WorkMinutes;
        var overtime = Math.Max(0, extra);
        var shortfall = Math.Max(0, -extra - excess);
        if (rules.ShortHoursRaises && shortfall > rules.ShortHoursMinutesOver)
        {
            exceptions.Add(AttendanceExceptionType.ShortHours);
        }

        var needsReview = exceptions.Count > 0 || (overtime > 0 && settings.PreApproval);
        return new ExpectedDay(exceptions, needsReview, true, working, overtime);
    }

    /// <summary>Adds up the minutes worked and the minutes on break of punches that go in, out, in, out, with a break-out and a break-in inside a session.</summary>
    private static (int Working, int OnBreak) Add(IReadOnlyList<PlannedPunch> typed)
    {
        var (working, onBreak) = (0, 0);
        var state = 0; // 0 off, 1 working, 2 on break
        var since = default(DateTime);
        foreach (var punch in typed)
        {
            switch (punch.Type, state)
            {
                case (NormalizedEventType.CheckIn, 0):
                    (state, since) = (1, punch.At);
                    break;
                case (NormalizedEventType.BreakOut, 1):
                    working += Minutes(since, punch.At);
                    (state, since) = (2, punch.At);
                    break;
                case (NormalizedEventType.BreakIn, 2):
                    onBreak += Minutes(since, punch.At);
                    (state, since) = (1, punch.At);
                    break;
                case (NormalizedEventType.CheckOut, 1):
                    working += Minutes(since, punch.At);
                    state = 0;
                    break;
                default:
                    throw new InvalidOperationException($"The oracle was given punches that are not a clean day: {punch.Type} at {punch.At:HH:mm} in state {state}.");
            }
        }

        return (working, onBreak);
    }

    private static int Minutes(DateTime from, DateTime to) => (int)Math.Floor((to - from).TotalMinutes);
}
