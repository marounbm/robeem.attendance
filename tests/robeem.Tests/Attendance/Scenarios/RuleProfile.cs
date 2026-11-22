using robeem.Contracts.Attendance;

namespace robeem.Tests.Attendance.Scenarios;

/// <summary>A combination of rules a tenant could have: changes to the standard rules, and the two policy settings that are not rules.</summary>
internal sealed class RuleProfile
{
    private RuleProfile(string name, Action<RuleSettings> change, bool sequential, bool preApproval)
    {
        Name = name;
        Change = change;
        Sequential = sequential;
        PreApproval = preApproval;
    }

    /// <summary>
    /// Gets the profiles the tests run: the starting rules, each switch on its own (turned the other way from how it starts), the usual
    /// groupings (all raise, none raise, all fixes, everything, loose and strict thresholds), the two policy settings, and random
    /// mixes of every switch and threshold (the same ones every run).
    /// </summary>
    public static IReadOnlyList<RuleProfile> All { get; } = Build();

    public string Name { get; }

    public Action<RuleSettings> Change { get; }

    /// <summary>Gets a value indicating whether the policy reads punches with no type in order.</summary>
    public bool Sequential { get; }

    /// <summary>Gets a value indicating whether overtime needs approval before the day is approved.</summary>
    public bool PreApproval { get; }

    private static List<RuleProfile> Build()
    {
        var profiles = new List<RuleProfile>
        {
            new("Starting rules", _ => { }, false, false),
            new("Long break raises", s => s.LongBreakRaises = true, false, false),
            new("Late break raises", s => s.LateBreakRaises = true, false, false),
            new("Early break raises", s => s.EarlyBreakRaises = true, false, false),
            new("No break does not raise", s => s.NoBreakRaises = false, false, false),
            new("Short hours do not raise", s => s.ShortHoursRaises = false, false, false),
            new("No-show does not raise", s => s.NoShowRaises = false, false, false),
            new("Short break is not work", s => s.ShortBreakIsWork = false, false, false),
            new("Gap is the break", s => s.GapIsBreak = true, false, false),
            new("Lone check-out is check-in", s => s.LoneCheckOutIsCheckIn = true, false, false),
            new("Lone check-in is check-out", s => s.LoneCheckInIsCheckOut = true, false, false),
            new("Morning double tap is one", s => s.MorningDuplicateIsOne = true, false, false),
            new("Evening double tap is one", s => s.EveningDuplicateIsOne = true, false, false),
            new("Break-in is break-out", s => s.BreakInIsBreakOut = true, false, false),
            new("Break-out is break-in", s => s.BreakOutIsBreakIn = true, false, false),
            new("Everything raises", AllRaise, false, false),
            new("Nothing raises", NoneRaise, false, false),
            new("All fixes", AllFixes, false, false),
            new("Everything raises and every fix", EverythingOn, false, false),
            new("Loose limits", LooseLimits, false, false),
            new("Strict limits", StrictLimits, false, false),
            new("Types read in order", _ => { }, true, false),
            new("Overtime needs approval", _ => { }, false, true),
            new("Everything, in order, with approval", EverythingOnKeepingShortBreaks, true, true),
        };

        // Random mixes: every switch flipped on a coin toss, and every threshold one of a few values. Fixed seed, so they are the same on every run.
        var random = new Random(20261122);
        int[] limits = [0, 5, 15, 30];
        for (var i = 1; i <= 12; i++)
        {
            var toss = Enumerable.Range(0, 14).Select(_ => random.Next(2) == 0).ToArray();
            var (longOver, lateOver, earlyOver, shortOver) = (limits[random.Next(4)], limits[random.Next(4)], limits[random.Next(4)], limits[random.Next(4)]);
            var (sequential, preApproval) = (random.Next(2) == 0, random.Next(3) == 0);
            profiles.Add(new RuleProfile(
                $"Random mix {i}",
                s =>
                {
                    s.LongBreakRaises = toss[0];
                    s.LateBreakRaises = toss[1];
                    s.EarlyBreakRaises = toss[2];
                    s.NoBreakRaises = toss[3];
                    s.ShortHoursRaises = toss[4];
                    s.NoShowRaises = toss[5];
                    s.ShortBreakIsWork = toss[6];
                    s.GapIsBreak = toss[7];
                    s.LoneCheckOutIsCheckIn = toss[8];
                    s.LoneCheckInIsCheckOut = toss[9];
                    s.MorningDuplicateIsOne = toss[10];
                    s.EveningDuplicateIsOne = toss[11];
                    s.BreakInIsBreakOut = toss[12];
                    s.BreakOutIsBreakIn = toss[13];
                    (s.LongBreakMinutesOver, s.LateBreakMinutesOver, s.EarlyBreakMinutesOver, s.ShortHoursMinutesOver) = (longOver, lateOver, earlyOver, shortOver);
                },
                sequential,
                preApproval));
        }

        return profiles;
    }

    private static void NoneRaise(RuleSettings s) =>
        (s.LongBreakRaises, s.LateBreakRaises, s.EarlyBreakRaises, s.NoBreakRaises, s.ShortHoursRaises, s.NoShowRaises) = (false, false, false, false, false, false);

    private static void EverythingOn(RuleSettings s)
    {
        AllRaise(s);
        AllFixes(s);
        s.GapIsBreak = true;
        s.ShortBreakIsWork = false;
    }

    private static void EverythingOnKeepingShortBreaks(RuleSettings s)
    {
        AllRaise(s);
        AllFixes(s);
        s.GapIsBreak = true;
    }

    private static void LooseLimits(RuleSettings s)
    {
        AllRaise(s);
        Limits(s, 30);
    }

    private static void StrictLimits(RuleSettings s)
    {
        AllRaise(s);
        Limits(s, 0);
        s.ShortBreakIsWork = false;
    }

    private static void AllRaise(RuleSettings s) =>
        (s.LongBreakRaises, s.LateBreakRaises, s.EarlyBreakRaises, s.NoBreakRaises, s.ShortHoursRaises, s.NoShowRaises) = (true, true, true, true, true, true);

    private static void AllFixes(RuleSettings s) =>
        (s.LoneCheckOutIsCheckIn, s.LoneCheckInIsCheckOut, s.MorningDuplicateIsOne, s.EveningDuplicateIsOne, s.BreakInIsBreakOut, s.BreakOutIsBreakIn) = (true, true, true, true, true, true);

    private static void Limits(RuleSettings s, int minutes) =>
        (s.LongBreakMinutesOver, s.LateBreakMinutesOver, s.EarlyBreakMinutesOver, s.ShortHoursMinutesOver) = (minutes, minutes, minutes, minutes);
}
