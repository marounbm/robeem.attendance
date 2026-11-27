using robeem.Application.Attendance;
using robeem.DataModel;

namespace robeem.Tests.Attendance;

/// <summary>An employee's pay period follows from their pay cycle: weekly, every two weeks, or monthly from a day, else the calendar month.</summary>
public sealed class PayCycleCalendarTests
{
    [Theory]
    [InlineData("2026-11-17", "2026-11-01", "2026-11-30")]
    [InlineData("2026-02-10", "2026-02-01", "2026-02-28")]
    [InlineData("2028-02-29", "2028-02-01", "2028-02-29")]
    [InlineData("2026-12-31", "2026-12-01", "2026-12-31")]
    public void WithNoCycle_ThePeriodIsTheCalendarMonth(string date, string start, string end)
    {
        Assert.Equal((D(start), D(end)), PayCycleCalendar.PeriodContaining(null, D(date)));
    }

    [Theory]
    [InlineData("2026-11-26", "2026-11-26", "2026-12-25")]
    [InlineData("2026-12-25", "2026-11-26", "2026-12-25")]
    [InlineData("2026-12-26", "2026-12-26", "2027-01-25")]
    [InlineData("2027-01-10", "2026-12-26", "2027-01-25")]
    [InlineData("2026-03-05", "2026-02-26", "2026-03-25")]
    public void AMonthlyCycleFromTheTwentySixth_RunsToThe25thOfTheNextMonth(string date, string start, string end)
    {
        Assert.Equal((D(start), D(end)), PayCycleCalendar.PeriodContaining(Cycle(PayCycleFrequency.Monthly, startDay: 26), D(date)));
    }

    [Fact]
    public void AMonthlyCycleFromTheFirst_IsTheCalendarMonth()
    {
        Assert.Equal((D("2026-02-01"), D("2026-02-28")), PayCycleCalendar.PeriodContaining(Cycle(PayCycleFrequency.Monthly, startDay: 1), D("2026-02-15")));
    }

    [Theory]
    [InlineData("2026-04-01", "2026-04-01", "2026-04-07")]
    [InlineData("2026-04-07", "2026-04-01", "2026-04-07")]
    [InlineData("2026-04-08", "2026-04-08", "2026-04-14")]
    [InlineData("2026-03-31", "2026-03-25", "2026-03-31")]
    [InlineData("2025-12-31", "2025-12-31", "2026-01-06")]
    public void AWeeklyCycle_RunsSevenDaysFromItsAnchor_AlsoBeforeTheAnchor(string date, string start, string end)
    {
        Assert.Equal((D(start), D(end)), PayCycleCalendar.PeriodContaining(Cycle(PayCycleFrequency.Weekly), D(date)));
    }

    [Theory]
    [InlineData("2026-04-14", "2026-04-01", "2026-04-14")]
    [InlineData("2026-04-15", "2026-04-15", "2026-04-28")]
    [InlineData("2026-03-20", "2026-03-18", "2026-03-31")]
    public void AnEveryTwoWeeksCycle_RunsFourteenDaysFromItsAnchor(string date, string start, string end)
    {
        Assert.Equal((D(start), D(end)), PayCycleCalendar.PeriodContaining(Cycle(PayCycleFrequency.EveryTwoWeeks), D(date)));
    }

    [Theory]
    [InlineData("2026-04-28", "2026-04-01", "2026-04-28")]
    [InlineData("2026-04-29", "2026-04-29", "2026-05-26")]
    [InlineData("2026-03-31", "2026-03-04", "2026-03-31")]
    public void AnEveryFourWeeksCycle_RunsTwentyEightDaysFromItsAnchor(string date, string start, string end)
    {
        Assert.Equal((D(start), D(end)), PayCycleCalendar.PeriodContaining(Cycle(PayCycleFrequency.EveryFourWeeks), D(date)));
    }

    [Theory]
    [InlineData(1, "2026-11-01", "2026-11-01", "2026-11-15")]
    [InlineData(1, "2026-11-15", "2026-11-01", "2026-11-15")]
    [InlineData(1, "2026-11-16", "2026-11-16", "2026-11-30")]
    [InlineData(1, "2026-02-20", "2026-02-16", "2026-02-28")]
    [InlineData(1, "2028-02-29", "2028-02-16", "2028-02-29")]
    [InlineData(1, "2026-12-31", "2026-12-16", "2026-12-31")]
    [InlineData(11, "2026-11-05", "2026-10-26", "2026-11-10")]
    [InlineData(11, "2026-11-11", "2026-11-11", "2026-11-25")]
    [InlineData(11, "2026-11-26", "2026-11-26", "2026-12-10")]
    [InlineData(11, "2027-01-05", "2026-12-26", "2027-01-10")]
    [InlineData(11, "2026-03-05", "2026-02-26", "2026-03-10")]
    [InlineData(13, "2026-02-28", "2026-02-28", "2026-03-12")]
    [InlineData(13, "2026-02-27", "2026-02-13", "2026-02-27")]
    public void ATwiceAMonthCycle_HasTwoPeriodsAMonth_FromItsStartDay(int startDay, string date, string start, string end)
    {
        Assert.Equal((D(start), D(end)), PayCycleCalendar.PeriodContaining(Cycle(PayCycleFrequency.SemiMonthly, startDay: startDay), D(date)));
    }

    [Fact]
    public void ATwiceAMonthCycle_CoversEveryDayOnce_WithNoGapAndNoOverlap()
    {
        foreach (var startDay in new[] { 1, 6, 11, 13 })
        {
            var cycle = Cycle(PayCycleFrequency.SemiMonthly, startDay: startDay);
            var (start, end) = PayCycleCalendar.PeriodContaining(cycle, D("2026-01-01"));
            for (var day = D("2026-01-01"); day <= D("2028-03-31"); day = day.AddDays(1))
            {
                // The period of a day contains it, and the next day is in the same period or in the one that starts right after.
                if (day > end)
                {
                    Assert.Equal(end.AddDays(1), PayCycleCalendar.PeriodContaining(cycle, day).Start);
                    (start, end) = PayCycleCalendar.PeriodContaining(cycle, day);
                }

                Assert.Equal((start, end), PayCycleCalendar.PeriodContaining(cycle, day));
                Assert.InRange(day, start, end);
            }
        }
    }

    [Theory]
    [InlineData(PayCycleFrequency.Weekly, "2026-11-12", "2026-11-04", "2026-11-10")]
    [InlineData(PayCycleFrequency.EveryFourWeeks, "2026-11-12", "2026-10-14", "2026-11-10")]
    [InlineData(PayCycleFrequency.SemiMonthly, "2026-11-12", "2026-10-13", "2026-10-27")]
    [InlineData(PayCycleFrequency.EveryTwoWeeks, "2026-11-12", "2026-10-28", "2026-11-10")]
    [InlineData(PayCycleFrequency.Monthly, "2026-11-12", "2026-09-26", "2026-10-25")]
    public void TheLastFinishedPeriod_IsTheOneBeforeTheOneThatContainsToday(PayCycleFrequency frequency, string today, string start, string end)
    {
        var cycle = Cycle(frequency, "2026-04-01", 26);
        Assert.Equal((D(start), D(end)), PayCycleCalendar.LastFinished(cycle, D(today)));
    }

    [Fact]
    public void OnTheLastDayOfAPeriod_ItIsNotFinished_TheNextDayItIs()
    {
        var cycle = Cycle(PayCycleFrequency.Monthly, startDay: 26);

        Assert.Equal(D("2026-11-25"), PayCycleCalendar.LastFinished(cycle, D("2026-12-25")).End);
        Assert.Equal(D("2026-12-25"), PayCycleCalendar.LastFinished(cycle, D("2026-12-26")).End);
    }

    [Fact]
    public void ACycleIsDescribedInWords()
    {
        Assert.Equal("Calendar month", PayCycleCalendar.Describe(null));
        Assert.Equal("Monthly, calendar month", PayCycleCalendar.Describe(Cycle(PayCycleFrequency.Monthly)));
        Assert.Equal("Monthly, from the 26th", PayCycleCalendar.Describe(Cycle(PayCycleFrequency.Monthly, startDay: 26)));
        Assert.Equal("Monthly, from the 22nd", PayCycleCalendar.Describe(Cycle(PayCycleFrequency.Monthly, startDay: 22)));
        Assert.Equal("Monthly, from the 13th", PayCycleCalendar.Describe(Cycle(PayCycleFrequency.Monthly, startDay: 13)));
        Assert.Equal("Weekly, from 2026-04-01 (Wed)", PayCycleCalendar.Describe(Cycle(PayCycleFrequency.Weekly)));
        Assert.Equal("Every 2 weeks, from 2026-04-01 (Wed)", PayCycleCalendar.Describe(Cycle(PayCycleFrequency.EveryTwoWeeks)));
        Assert.Equal("Every 4 weeks, from 2026-04-01 (Wed)", PayCycleCalendar.Describe(Cycle(PayCycleFrequency.EveryFourWeeks)));
        Assert.Equal("Twice a month: 1st to 15th, 16th to the end of the month", PayCycleCalendar.Describe(Cycle(PayCycleFrequency.SemiMonthly)));
        Assert.Equal("Twice a month: 11th to 25th, 26th to the 10th of the next month", PayCycleCalendar.Describe(Cycle(PayCycleFrequency.SemiMonthly, startDay: 11)));
    }

    private static DateTime D(string iso) => DateTime.ParseExact(iso, "yyyy-MM-dd", global::System.Globalization.CultureInfo.InvariantCulture);

    private static PayCycle Cycle(PayCycleFrequency frequency, string anchor = "2026-04-01", int startDay = 1) =>
        new() { Frequency = frequency, AnchorDate = D(anchor), StartDay = startDay };
}
