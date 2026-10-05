using robeem.Application.Attendance;
using robeem.DataModel;

namespace robeem.Tests.Attendance;

/// <summary>
/// Exceptions on the shapes of shift that need care (P14 gap 7): an overnight shift, whose end falls
/// on the next day, and a split shift, which has several records on one day.
/// </summary>
public sealed class AttendanceSpecialShiftTests : AttendanceEngineTestBase
{
    private static readonly DateTime Tuesday = Monday.AddDays(1);

    [Fact]
    public async Task OvernightShift_ACheckInWithNoCheckOut_IsNotMissingWhileTheShiftAndItsGraceRun()
    {
        AssignSchedule(new ShiftSpec("22:00", "06:00"));
        AddPunch(NormalizedEventType.CheckIn, Monday.At(22, 0));

        // The shift ends Tuesday 06:00; with the 120-minute grace it is due from 08:00 on Tuesday.
        Clock.Local = Tuesday.At(7, 59);
        var early = await ProcessAsync(Monday);

        Assert.Equal(0, early.ExceptionsRaised);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceExceptions);
        Assert.Equal(AttendanceRecordStatus.PendingReview, Assert.Single(db.AttendanceRecords).Status);
    }

    [Fact]
    public async Task OvernightShift_ACheckInWithNoCheckOut_IsRaisedForTheStartDate_OnceTheGraceHasElapsed()
    {
        AssignSchedule(new ShiftSpec("22:00", "06:00"));
        AddPunch(NormalizedEventType.CheckIn, Monday.At(22, 0));

        Clock.Local = Tuesday.At(8, 0);
        var result = await ProcessAsync(Monday);

        Assert.Equal(1, result.ExceptionsRaised);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var exception = Assert.Single(db.AttendanceExceptions);
        Assert.Equal((AttendanceExceptionType.MissingPunch, NormalizedEventType.CheckOut, Monday), (exception.ExceptionType, exception.MissingEventType, exception.AttendanceDate));

        // Tuesday has no shift of its own: processing it raises nothing and takes nothing of Monday's.
        Assert.Equal(new DayProcessingResult(0, 0, 0), await ProcessAsync(Tuesday));
        Assert.Single(db.AttendanceExceptions);
    }

    [Fact]
    public async Task OvernightShift_TheCheckOutArrivingTheNextMorning_ClosesTheException()
    {
        AssignSchedule(new ShiftSpec("22:00", "06:00"));
        AddPunch(NormalizedEventType.CheckIn, Monday.At(22, 0));
        Clock.Local = Tuesday.At(9, 0);
        await ProcessAsync(Monday);

        // The device uploads the check-out late.
        AddPunch(NormalizedEventType.CheckOut, Tuesday.At(6, 5));
        var result = await ProcessAsync(Monday);

        Assert.Equal(1, result.ExceptionsResolved);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var exception = Assert.Single(db.AttendanceExceptions);
        Assert.Equal((AttendanceExceptionStatus.Closed, AttendanceExceptionResolutionMethod.AutoResolved), (exception.Status, exception.ResolutionMethod));
        var record = db.AttendanceRecords.Single(r => r.IsCurrent);
        Assert.Equal((AttendanceRecordStatus.Approved, new TimeSpan(8, 5, 0), 5), (record.Status, record.WorkingHours, record.OvertimeMinutes));
    }

    [Fact]
    public async Task OvernightShift_AnInvalidSequenceAfterMidnight_BelongsToTheStartDate()
    {
        AssignSchedule(new ShiftSpec("22:00", "06:00"));
        AddPunch(NormalizedEventType.CheckIn, Monday.At(22, 0));
        AddPunch(NormalizedEventType.CheckOut, Tuesday.At(6, 0));
        AddPunch(NormalizedEventType.CheckOut, Tuesday.At(6, 30));
        Clock.Local = Tuesday.At(12, 0);

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var exception = Assert.Single(db.AttendanceExceptions);
        Assert.Equal((AttendanceExceptionType.InvalidSequence, Monday), (exception.ExceptionType, exception.AttendanceDate));
    }

    [Fact]
    public async Task SplitShift_AMissingCheckOutOfTheSecondShift_IsRaisedForThatShiftOnly()
    {
        AssignSchedule(new ShiftSpec("08:00", "12:00"), new ShiftSpec("14:00", "18:00"));
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.CheckIn, Monday.At(14, 0));
        Clock.Local = Monday.At(21, 0);

        var result = await ProcessAsync(Monday);

        Assert.Equal(1, result.ExceptionsRaised);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var secondShiftId = db.AttendanceRecords.Single(r => r.ScheduledStart == Monday.At(14, 0)).TimeScheduleShiftId;
        var exception = Assert.Single(db.AttendanceExceptions);
        Assert.Equal((NormalizedEventType.CheckOut, secondShiftId), (exception.MissingEventType, exception.TimeScheduleShiftId));

        // The first shift is whole and approved; the second waits for a person; the day follows the weaker one.
        var records = db.AttendanceRecords.OrderBy(r => r.ScheduledStart).ToList();
        Assert.Equal([AttendanceRecordStatus.Approved, AttendanceRecordStatus.PendingReview], records.Select(r => r.Status));
        var summary = db.AttendanceDaySummaries.Single();
        Assert.Equal((AttendanceRecordStatus.PendingReview, 1, 2), (summary.Status, summary.ExceptionCount, summary.ShiftCount));
    }

    [Fact]
    public async Task SplitShift_APunchBetweenTheShifts_GoesToTheNearerOne_NotToBoth()
    {
        AssignSchedule(new ShiftSpec("08:00", "12:00"), new ShiftSpec("14:00", "18:00"));
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(12, 30));
        AddPunch(NormalizedEventType.CheckIn, Monday.At(13, 45));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(18, 0));
        Clock.Local = Monday.At(21, 0);

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceExceptions);
        var records = db.AttendanceRecords.OrderBy(r => r.ScheduledStart).ToList();
        Assert.Equal((Monday.At(12, 30), Monday.At(13, 45)), (records[0].ActualCheckOut, records[1].ActualCheckIn));
        Assert.Equal([new TimeSpan(4, 30, 0), new TimeSpan(4, 15, 0)], records.Select(r => r.WorkingHours));
    }

    [Fact]
    public async Task SplitShift_AShiftWithNoPunchAtAll_IsANoShowForThatShiftOnly()
    {
        AssignSchedule(new ShiftSpec("08:00", "12:00"), new ShiftSpec("14:00", "18:00"));
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(12, 0));
        Clock.Local = Monday.At(21, 0);

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var secondShiftId = db.AttendanceRecords.Single(r => r.ScheduledStart == Monday.At(14, 0)).TimeScheduleShiftId;
        var raised = db.AttendanceExceptions.ToList();
        Assert.Equal([NormalizedEventType.CheckIn, NormalizedEventType.CheckOut], raised.Select(e => e.MissingEventType!.Value).OrderBy(t => t));
        Assert.All(raised, e => Assert.Equal(secondShiftId, e.TimeScheduleShiftId));
    }
}
