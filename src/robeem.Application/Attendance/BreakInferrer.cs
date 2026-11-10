using robeem.Application.HR.Absence;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Reads a break out of the punches of a device that sends only check-in and check-out (CLAUDE.md §1.6). It only retypes punches that
/// exist, marks them <see cref="AttendancePunchSource.Inferred"/>, and never adds one. Pure: no I/O. Whether a gap is the break is the
/// rules' to say; this finds the candidate gaps and their facts.
/// </summary>
internal static class BreakInferrer
{
    /// <summary>
    /// On a shift with a scheduled break, a day whose punches are all check-ins and check-outs, alternating from a check-in and ending
    /// with a check-out, has its internal check-out / check-in gaps looked at. A gap shorter than <paramref name="minMinutes"/> is a short
    /// break (its punches are retyped so the existing rule leaves the time as work). A gap the rules read as the break is retyped when
    /// exactly one gap is. Otherwise nothing is read.
    /// </summary>
    /// <param name="typed">The punches in time order, without repeats.</param>
    /// <param name="shift">The scheduled shift; null (work outside a shift) infers nothing.</param>
    /// <param name="rules">The rules that say whether a gap is the break.</param>
    /// <param name="minMinutes">The shortest gap that is a break; a shorter one is a toilet visit (0: none is).</param>
    /// <param name="excused">The part-day leave that overlaps the shift.</param>
    public static List<AttendancePunch> Infer(List<AttendancePunch> typed, ResolvedShift? shift, RuleSetSnapshot rules, int minMinutes, IReadOnlyList<ExcusedPeriod> excused)
    {
        if (shift is not { BreakTime: true, BreakStartsAt: { } scheduledStart } || !IsAlternatingInOut(typed))
        {
            return typed;
        }

        var gaps = new List<int>();
        var shortGaps = new List<int>();
        for (var i = 1; i + 1 < typed.Count; i += 2)
        {
            var (leaving, returning) = (typed[i], typed[i + 1]);
            var length = returning.Utc - leaving.Utc;
            var excusedPercent = ExcusedPercent(excused, leaving.Local, returning.Local);
            if (excusedPercent > 50)
            {
                continue;
            }

            if (minMinutes > 0 && length < TimeSpan.FromMinutes(minMinutes))
            {
                shortGaps.Add(i);
                continue;
            }

            var facts = new RuleFacts
            {
                ShiftHasBreak = true,
                ShiftIsOvernight = shift.EndsAt.Date > shift.StartsAt.Date,
                GapMinutes = (int)Math.Ceiling(length.TotalMinutes),
                GapStartOffsetMinutes = (int)Math.Ceiling(Math.Abs((leaving.Local - scheduledStart).TotalMinutes)),
                GapExcusedPercent = excusedPercent,
            };
            if (rules.InfersBreak(facts))
            {
                gaps.Add(i);
            }
        }

        var retype = gaps.Count == 1 ? shortGaps.Append(gaps[0]).ToList() : shortGaps;
        if (retype.Count == 0)
        {
            return typed;
        }

        var result = new List<AttendancePunch>(typed);
        foreach (var i in retype)
        {
            result[i] = result[i] with { Type = NormalizedEventType.BreakOut, Source = AttendancePunchSource.Inferred };
            result[i + 1] = result[i + 1] with { Type = NormalizedEventType.BreakIn, Source = AttendancePunchSource.Inferred };
        }

        return result;
    }

    /// <summary>The share of the gap from <paramref name="from"/> to <paramref name="to"/> that part-day leave covers, in percent, rounded up.</summary>
    private static int ExcusedPercent(IReadOnlyList<ExcusedPeriod> excused, DateTime from, DateTime to)
    {
        var total = (to - from).TotalMinutes;
        return total <= 0 ? 0 : (int)Math.Ceiling(ExcusedTime.UnionMinutes(excused, from, to) * 100.0 / total);
    }

    private static bool IsAlternatingInOut(List<AttendancePunch> typed)
    {
        if (typed.Count < 4 || typed.Count % 2 != 0)
        {
            return false;
        }

        for (var i = 0; i < typed.Count; i++)
        {
            var expected = i % 2 == 0 ? NormalizedEventType.CheckIn : NormalizedEventType.CheckOut;
            if (typed[i].Type != expected)
            {
                return false;
            }
        }

        return true;
    }
}
