using System.Globalization;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Turns a pay cycle into pay periods. Pure: periods are computed from the cycle, never stored. With no cycle at all the period is the
/// calendar month.
/// </summary>
public static class PayCycleCalendar
{
    /// <summary>The latest day of the month a monthly period can start on, so that every month has it.</summary>
    public const int MaxStartDay = 28;

    /// <summary>The latest start day of a twice-a-month cycle: its second period starts 15 days later, and every month has that day.</summary>
    public const int MaxSemiMonthlyStartDay = 13;

    /// <summary>Gets the period (first and last day, inclusive) of the cycle that contains the date.</summary>
    public static (DateTime Start, DateTime End) PeriodContaining(PayCycle? cycle, DateTime date)
    {
        var day = date.Date;
        if (cycle is null)
        {
            return Monthly(1, day);
        }

        switch (cycle.Frequency)
        {
            case PayCycleFrequency.Weekly:
                return Fixed(cycle.AnchorDate.Date, 7, day);
            case PayCycleFrequency.EveryTwoWeeks:
                return Fixed(cycle.AnchorDate.Date, 14, day);
            case PayCycleFrequency.EveryFourWeeks:
                return Fixed(cycle.AnchorDate.Date, 28, day);
            case PayCycleFrequency.SemiMonthly:
                return SemiMonthly(Math.Clamp(cycle.StartDay, 1, MaxSemiMonthlyStartDay), day);
            default:
                return Monthly(Math.Clamp(cycle.StartDay, 1, MaxStartDay), day);
        }
    }

    /// <summary>Gets the most recent period that has ended before <paramref name="today"/>: the one before the period that contains it.</summary>
    public static (DateTime Start, DateTime End) LastFinished(PayCycle? cycle, DateTime today) =>
        PeriodContaining(cycle, PeriodContaining(cycle, today).Start.AddDays(-1));

    /// <summary>Describes a cycle in words, for the screens: "Monthly, from the 26th", "Every 2 weeks from 2026-04-01 (Wed)".</summary>
    public static string Describe(PayCycle? cycle) => cycle is null
        ? "Calendar month"
        : cycle.Frequency switch
        {
            PayCycleFrequency.Weekly => $"Weekly, from {Day(cycle.AnchorDate)}",
            PayCycleFrequency.EveryTwoWeeks => $"Every 2 weeks, from {Day(cycle.AnchorDate)}",
            PayCycleFrequency.EveryFourWeeks => $"Every 4 weeks, from {Day(cycle.AnchorDate)}",
            PayCycleFrequency.SemiMonthly => DescribeSemiMonthly(Math.Clamp(cycle.StartDay, 1, MaxSemiMonthlyStartDay)),
            _ => cycle.StartDay == 1 ? "Monthly, calendar month" : $"Monthly, from the {Ordinal(cycle.StartDay)}",
        };

    private static string DescribeSemiMonthly(int startDay) => startDay == 1
        ? "Twice a month: 1st to 15th, 16th to the end of the month"
        : $"Twice a month: {Ordinal(startDay)} to {Ordinal(startDay + 14)}, {Ordinal(startDay + 15)} to the {Ordinal(startDay - 1)} of the next month";

    private static (DateTime Start, DateTime End) SemiMonthly(int startDay, DateTime day)
    {
        // Two periods start in each month: on the start day (running 15 days) and 15 days later (running to the day before the start day of the next month).
        var second = startDay + 15;
        if (day.Day >= second)
        {
            return (new DateTime(day.Year, day.Month, second), new DateTime(day.Year, day.Month, startDay).AddMonths(1).AddDays(-1));
        }

        if (day.Day >= startDay)
        {
            var first = new DateTime(day.Year, day.Month, startDay);
            return (first, first.AddDays(14));
        }

        // Before the start day: the second period that began in the month before.
        var previous = new DateTime(day.Year, day.Month, startDay).AddMonths(-1);
        return (new DateTime(previous.Year, previous.Month, second), new DateTime(day.Year, day.Month, startDay).AddDays(-1));
    }

    private static string Day(DateTime date) =>
        $"{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} ({date.ToString("ddd", CultureInfo.InvariantCulture)})";

    private static (DateTime Start, DateTime End) Fixed(DateTime anchor, int length, DateTime day)
    {
        // Floor division, so a date before the anchor still lands in the period that contains it.
        var days = (day - anchor).Days;
        var index = (int)Math.Floor(days / (double)length);
        var start = anchor.AddDays(index * length);
        return (start, start.AddDays(length - 1));
    }

    private static (DateTime Start, DateTime End) Monthly(int startDay, DateTime day)
    {
        var first = day.Day >= startDay
            ? new DateTime(day.Year, day.Month, startDay)
            : new DateTime(day.Year, day.Month, startDay).AddMonths(-1);
        return (first, first.AddMonths(1).AddDays(-1));
    }

    private static string Ordinal(int n) => (n % 100) switch
    {
        11 or 12 or 13 => $"{n}th",
        _ => (n % 10) switch
        {
            1 => $"{n}st",
            2 => $"{n}nd",
            3 => $"{n}rd",
            _ => $"{n}th",
        },
    };
}
