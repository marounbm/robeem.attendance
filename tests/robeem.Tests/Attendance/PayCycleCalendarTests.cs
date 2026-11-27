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
    [InlineData(PayCycleFrequency.Weekly, "2026-11-12", "2026-11-04", "2026-11-10")]
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
    }

    private static DateTime D(string iso) => DateTime.ParseExact(iso, "yyyy-MM-dd", global::System.Globalization.CultureInfo.InvariantCulture);

    private static PayCycle Cycle(PayCycleFrequency frequency, string anchor = "2026-04-01", int startDay = 1) =>
        new() { Frequency = frequency, AnchorDate = D(anchor), StartDay = startDay };
}
