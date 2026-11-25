using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>
/// Shows the two rules that read the same pair of punches (keep the first, keep the last) as one choice with one pair of numbers, and writes the choice
/// back into the two rules. They can't both be on, so the form never offers the choice of both. The numbers belong to the pair: saving gives both rules the
/// numbers shown, and a pair read by keeping the last shows that rule's numbers. For a check-in then a check-out at the end, "the first" reads the check-in as
/// the check-out and "the last" leaves the check-in out and keeps the check-out.
/// </summary>
public static class PunchPairChoices
{
    /// <summary>Reads the choice of each pair; the first rule wins when both are on, as it does when days are read.</summary>
    public static PunchPairReading Read(RuleSettings settings) => new(
        settings.MorningDuplicateIsOne ? PairKeeps.First : settings.MorningLastPunchCounts ? PairKeeps.Last : PairKeeps.Nothing,
        settings.EveningDuplicateIsOne ? PairKeeps.First : settings.EveningLastPunchCounts ? PairKeeps.Last : PairKeeps.Nothing,
        settings.EveningInThenOutIsOne ? PairKeeps.First : settings.EveningInThenOutLast ? PairKeeps.Last : PairKeeps.Nothing,
        settings.RepeatedCheckInFirst ? PairKeeps.First : settings.RepeatedCheckInLast ? PairKeeps.Last : PairKeeps.Nothing,
        settings.RepeatedCheckOutFirst ? PairKeeps.First : settings.RepeatedCheckOutLast ? PairKeeps.Last : PairKeeps.Nothing);

    /// <summary>Puts the numbers of the rule that is on into the fields the form shows (the keep-first rule's), so that the form shows what the days are read by.</summary>
    public static void ShowSharedNumbers(RuleSettings settings)
    {
        if (!settings.MorningDuplicateIsOne && settings.MorningLastPunchCounts)
        {
            (settings.MorningDuplicateWindowMinutes, settings.MorningDuplicateGapMinutes) = (settings.MorningLastPunchWindowMinutes, settings.MorningLastPunchGapMinutes);
        }

        if (!settings.EveningDuplicateIsOne && settings.EveningLastPunchCounts)
        {
            (settings.EveningDuplicateWindowMinutes, settings.EveningDuplicateGapMinutes) = (settings.EveningLastPunchWindowMinutes, settings.EveningLastPunchGapMinutes);
        }

        if (!settings.EveningInThenOutIsOne && settings.EveningInThenOutLast)
        {
            (settings.EveningInThenOutWindowMinutes, settings.EveningInThenOutGapMinutes) = (settings.EveningInThenOutLastWindowMinutes, settings.EveningInThenOutLastGapMinutes);
        }
    }

    /// <summary>Writes the choices into the two rules of each pair, with the numbers the form showed.</summary>
    public static void Apply(RuleSettings settings, PunchPairReading choices)
    {
        settings.MorningDuplicateIsOne = choices.Morning == PairKeeps.First;
        settings.MorningLastPunchCounts = choices.Morning == PairKeeps.Last;
        (settings.MorningLastPunchWindowMinutes, settings.MorningLastPunchGapMinutes) = (settings.MorningDuplicateWindowMinutes, settings.MorningDuplicateGapMinutes);

        settings.EveningDuplicateIsOne = choices.Evening == PairKeeps.First;
        settings.EveningLastPunchCounts = choices.Evening == PairKeeps.Last;
        (settings.EveningLastPunchWindowMinutes, settings.EveningLastPunchGapMinutes) = (settings.EveningDuplicateWindowMinutes, settings.EveningDuplicateGapMinutes);

        settings.EveningInThenOutIsOne = choices.InThenOut == PairKeeps.First;
        settings.EveningInThenOutLast = choices.InThenOut == PairKeeps.Last;
        (settings.EveningInThenOutLastWindowMinutes, settings.EveningInThenOutLastGapMinutes) = (settings.EveningInThenOutWindowMinutes, settings.EveningInThenOutGapMinutes);

        settings.RepeatedCheckInFirst = choices.RepeatedCheckIn == PairKeeps.First;
        settings.RepeatedCheckInLast = choices.RepeatedCheckIn == PairKeeps.Last;
        settings.RepeatedCheckOutFirst = choices.RepeatedCheckOut == PairKeeps.First;
        settings.RepeatedCheckOutLast = choices.RepeatedCheckOut == PairKeeps.Last;
    }

    /// <summary>The field of the form that shows a number kept for both rules of a pair: the keep-last rule's number is shown in the keep-first rule's field.</summary>
    public static string ShownField(string field) => field
        .Replace("MorningLastPunchWindowMinutes", "MorningDuplicateWindowMinutes", StringComparison.Ordinal)
        .Replace("MorningLastPunchGapMinutes", "MorningDuplicateGapMinutes", StringComparison.Ordinal)
        .Replace("EveningLastPunchWindowMinutes", "EveningDuplicateWindowMinutes", StringComparison.Ordinal)
        .Replace("EveningLastPunchGapMinutes", "EveningDuplicateGapMinutes", StringComparison.Ordinal)
        .Replace("EveningInThenOutLastWindowMinutes", "EveningInThenOutWindowMinutes", StringComparison.Ordinal)
        .Replace("EveningInThenOutLastGapMinutes", "EveningInThenOutGapMinutes", StringComparison.Ordinal);
}
