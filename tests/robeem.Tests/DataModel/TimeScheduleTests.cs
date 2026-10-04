using robeem.DataModel;

namespace robeem.Tests.DataModel;

public sealed class TimeScheduleTests
{
    [Theory]
    [InlineData(2026, 10, 5, 1)] // Monday
    [InlineData(2026, 10, 9, 5)] // Friday
    [InlineData(2026, 10, 11, 7)] // Sunday
    [InlineData(2026, 10, 12, 1)] // next Monday
    public void GetCycleDay_WeeklyScheduleStartingMonday_NumbersDaysFromMonday(int year, int month, int day, int expected)
    {
        var schedule = new TimeSchedule { CyclePeriod = 7, FirstCycleDay = DayOfWeek.Monday };

        Assert.Equal(expected, schedule.GetCycleDay(new DateTime(year, month, day)));
    }
}
