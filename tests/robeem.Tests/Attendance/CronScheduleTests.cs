using robeem.Application.Attendance;

namespace robeem.Tests.Attendance;

public sealed class CronScheduleTests
{
    private static readonly TimeSpan NoOffset = TimeSpan.Zero;

    [Theory]
    [InlineData("*/5 * * * *", true)]
    [InlineData("0 2 * * *", true)]
    [InlineData("30 0 2 * * *", true)]
    [InlineData("not a cron", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParse_AcceptsFiveAndSixFieldExpressions(string? cron, bool valid) =>
        Assert.Equal(valid, CronSchedule.TryParse(cron, out _));

    [Fact]
    public void IsDue_WhenThereHasBeenNoRun()
    {
        Assert.True(CronSchedule.IsDue("*/5 * * * *", null, At(10, 1), TimeZoneInfo.Utc));
    }

    [Fact]
    public void IsDue_OnlyOnceTheNextOccurrenceAfterTheLastRunHasPassed()
    {
        Assert.False(CronSchedule.IsDue("*/5 * * * *", At(10, 0), At(10, 4), TimeZoneInfo.Utc));
        Assert.True(CronSchedule.IsDue("*/5 * * * *", At(10, 0), At(10, 5), TimeZoneInfo.Utc));
        Assert.True(CronSchedule.IsDue("*/5 * * * *", At(10, 0), At(11, 0), TimeZoneInfo.Utc));
    }

    [Fact]
    public void IsDue_IsNeverTrueForAnExpressionThatDoesNotParse()
    {
        Assert.False(CronSchedule.IsDue("garbage", null, At(10, 0), TimeZoneInfo.Utc));
    }

    [Fact]
    public void Next_IsTheFirstOccurrenceAfterTheGivenTime()
    {
        Assert.Equal(At(11, 0).AddDays(0), CronSchedule.Next("0 11 * * *", At(10, 0), TimeZoneInfo.Utc));
        Assert.Null(CronSchedule.Next("garbage", At(10, 0), TimeZoneInfo.Utc));
    }

    private static DateTimeOffset At(int hour, int minute) => new(2026, 10, 5, hour, minute, 0, NoOffset);
}
