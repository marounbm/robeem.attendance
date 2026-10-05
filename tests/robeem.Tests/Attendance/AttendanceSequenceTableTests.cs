using robeem.DataModel;

namespace robeem.Tests.Attendance;

/// <summary>
/// The decision table of study §14.1 for punches in the wrong order, one row per case (P14). Each
/// case is a day's punches as <c>type@HH:mm</c>, with the one exception the detector must raise and
/// the punch it must name. The two tables are those of <c>AttendanceExceptionDetector</c>.
/// </summary>
public sealed class AttendanceSequenceTableTests : AttendanceEngineTestBase
{
    private const string FullDay = "I@08:00,BO@12:00,BI@13:00,O@17:00";

    public AttendanceSequenceTableTests()
    {
        // Long after the shift and its grace: nothing here is "not yet due".
        Clock.Local = Monday.At(23, 0);
    }

    [Theory]
    [InlineData("I@08:00,BO@12:00,I@12:30,BI@13:00,O@17:00", "12:30", "a check-in during a break")]
    [InlineData("I@08:00,BO@12:00,BO@12:30,BI@13:00,O@17:00", "12:30", "a break-out after a break-out")]
    [InlineData("BO@07:00,I@08:00,BO@12:00,BI@13:00,O@17:00", "07:00", "a break-out before the first check-in")]
    [InlineData("I@08:00,BO@12:00,BI@13:00,O@17:00,BO@17:30", "17:30", "a break-out after the check-out")]
    [InlineData("BI@07:00,I@08:00,BO@12:00,BI@13:00,O@17:00", "07:00", "a break-in before the first check-in")]
    [InlineData("I@08:00,BO@12:00,BI@13:00,O@17:00,BI@17:30", "17:30", "a break-in after the check-out")]
    [InlineData("I@08:00,BO@12:00,BI@13:00,O@17:00,O@17:30", "17:30", "a check-out after a check-out")]
    [InlineData("O@07:30,I@08:00,BO@12:00,BI@13:00,O@17:00", "07:30", "a check-out before the first check-in")]
    [InlineData("I@08:00,I@08:10,BO@12:00,BI@13:00,O@17:00", "08:10", "a check-in after a check-in, outside the window")]
    public async Task OnAShiftWithABreak_APunchOutOfOrder_IsOneInvalidSequence_NamingThatPunch(string punches, string strayAt, string because)
    {
        AssignSchedule(StandardDay);
        AddPunches(punches);

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var exception = Assert.Single(db.AttendanceExceptions);
        Assert.True(exception.ExceptionType == AttendanceExceptionType.InvalidSequence, $"Expected an invalid sequence for {because}.");
        Assert.Equal(AttendanceExceptionStatus.Open, exception.Status);
        var stray = db.NormalizedAttendanceTransactions.Single(n => n.Id == exception.RelatedNormalizedTransactionId);
        Assert.Equal(Monday.At(int.Parse(strayAt[..2]), int.Parse(strayAt[3..])), stray.EventTimestampLocal);

        // An open exception always keeps the day from being approved (CLAUDE.md §1.5).
        Assert.All(db.AttendanceRecords.Where(r => r.IsCurrent), r => Assert.Equal(AttendanceRecordStatus.PendingReview, r.Status));
    }

    [Fact]
    public async Task OnAShiftWithoutABreak_ABreakOutNothingClosed_IsAnInvalidSequence_NotAMissingBreakIn()
    {
        AssignSchedule(new ShiftSpec("08:00", "17:00"));
        AddPunches("I@08:00,BO@12:00,O@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var exception = Assert.Single(db.AttendanceExceptions);
        Assert.Equal((AttendanceExceptionType.InvalidSequence, null), (exception.ExceptionType, exception.MissingEventType));
        Assert.Equal(Monday.At(12, 0), db.NormalizedAttendanceTransactions.Single(n => n.Id == exception.RelatedNormalizedTransactionId).EventTimestampLocal);
    }

    [Theory]
    [InlineData("I@08:00,I@08:01,BO@12:00,BI@13:00,O@17:00", "a check-in")]
    [InlineData("I@08:00,BO@12:00,BO@12:01,BI@13:00,O@17:00", "a break-out")]
    [InlineData("I@08:00,BO@12:00,BI@13:00,BI@13:01,O@17:00", "a break-in")]
    [InlineData("I@08:00,BO@12:00,BI@13:00,O@17:00,O@17:01", "a check-out")]
    public async Task APunchRepeatedInsideTheDuplicateWindow_IsOnePunch_OfEveryType(string punches, string repeated)
    {
        AssignSchedule(StandardDay);
        AddPunches(punches);

        var result = await ProcessAsync(Monday);

        Assert.True(result.ExceptionsRaised == 0, $"Repeating {repeated} inside the window must not raise an exception.");
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceExceptions);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((TimeSpan.FromHours(8), TimeSpan.FromHours(1), AttendanceRecordStatus.Approved), (record.WorkingHours, record.BreakDuration, record.Status));
    }

    [Fact]
    public async Task ACleanDay_RaisesNothing()
    {
        AssignSchedule(StandardDay);
        AddPunches(FullDay);

        var result = await ProcessAsync(Monday);

        Assert.Equal(0, result.ExceptionsRaised);
    }

    private void AddPunches(string punches)
    {
        foreach (var punch in punches.Split(','))
        {
            var parts = punch.Split('@');
            var type = parts[0] switch
            {
                "I" => NormalizedEventType.CheckIn,
                "O" => NormalizedEventType.CheckOut,
                "BO" => NormalizedEventType.BreakOut,
                "BI" => NormalizedEventType.BreakIn,
                _ => throw new ArgumentException($"Unknown punch type {parts[0]}."),
            };
            AddPunch(type, Monday.At(int.Parse(parts[1][..2]), int.Parse(parts[1][3..])));
        }
    }
}
