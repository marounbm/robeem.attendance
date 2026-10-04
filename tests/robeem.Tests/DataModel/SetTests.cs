using robeem.DataModel;

namespace robeem.Tests.DataModel;

public sealed class SetTests
{
    [Fact]
    public void DayShift_WorkAndTotalHours_AccountForBreak()
    {
        var shift = new TimeScheduleShift
        {
            StartTime = "09:00",
            EndTime = "17:00",
            BreakTime = true,
            BreakStartTime = "13:00",
            BreakEndTime = "14:00",
        };

        Assert.Equal(8, shift.WorkHours);
        Assert.Equal(1, shift.BreakHours);
        Assert.Equal(7, shift.TotalHours);
    }

    [Fact]
    public void OvernightShift_EndBeforeStart_CountsIntoNextDay()
    {
        Assert.Equal(8, Set.TimeSpan("22:00", "06:00"));
    }

    [Fact]
    public void BreakHours_AreZero_WhenBreakTimeIsOff()
    {
        var shift = new TimeScheduleShift { BreakTime = false, BreakStartTime = "12:00", BreakEndTime = "13:00" };

        Assert.Equal(0, shift.BreakHours);
        Assert.Equal(shift.WorkHours, shift.TotalHours);
    }

    [Theory]
    [InlineData("24:00")]
    [InlineData("9h")]
    [InlineData("")]
    public void InvalidTime_IsRejected(string time)
    {
        Assert.False(Set.IsValidTime(time));
        Assert.Throws<FormatException>(() => Set.asTime(time));
    }

    [Fact]
    public void AsTime_KeepsTimeOfDay()
    {
        Assert.Equal(new TimeSpan(7, 5, 0), Set.asTime("7:05").TimeOfDay);
    }

    [Fact]
    public void EntityKindHelpers_ReflectBaseClassAndMarker()
    {
        Assert.True(new TimeScheduleLine().isSystemEntity());
        Assert.False(new TimeSchedule().isSystemEntity());
        Assert.False(new Profile().isWorkflowEntity());
    }
}
