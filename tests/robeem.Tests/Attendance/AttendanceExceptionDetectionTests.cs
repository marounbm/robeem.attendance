using robeem.Application.Attendance;
using robeem.DataModel;

namespace robeem.Tests.Attendance;

/// <summary>
/// Exception detection (study §9, §14.1) through the processing engine. The study is not in the
/// repository: the Scenario 3 inputs are reconstructed from the outcome Prompt 9 quotes (a break-out
/// to be corrected to 12:00 on the 08:00-17:00 day with a 12:00-13:00 break), see ATTENDANCE_P8_NOTES.md.
/// </summary>
public sealed class AttendanceExceptionDetectionTests : AttendanceEngineTestBase
{
    // Default grace is 120 minutes, so a missing punch of the standard day is due from 19:00.
    private static readonly DateTime DueFrom = Monday.At(19, 0);

    [Fact]
    public async Task Scenario3_MissingBreakOut_RaisesOneOpenException_AndTheRecordNeedsReview()
    {
        AssignSchedule(StandardDay);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));

        var result = await ProcessAsync(Monday);

        Assert.Equal(1, result.ExceptionsRaised);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var exception = Assert.Single(db.AttendanceExceptions);
        Assert.Equal(AttendanceExceptionType.MissingPunch, exception.ExceptionType);
        Assert.Equal(NormalizedEventType.BreakOut, exception.MissingEventType);
        Assert.Equal(AttendanceExceptionStatus.Open, exception.Status);
        Assert.Equal((Monday, EmployeeId), (exception.AttendanceDate, exception.EmployeeId));
        Assert.NotNull(exception.TimeScheduleShiftId);
        Assert.Null(exception.RelatedNormalizedTransactionId);
        Assert.Null(exception.ResolutionMethod);

        // No break-out is invented: the record has none, and no break is deducted.
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Null(record.ActualBreakOut);
        Assert.Equal(TimeSpan.FromHours(9), record.WorkingHours);
        Assert.Equal(AttendanceRecordStatus.PendingReview, record.Status);

        var summary = Assert.Single(db.AttendanceDaySummaries);
        Assert.Equal((1, AttendanceRecordStatus.PendingReview), (summary.ExceptionCount, summary.Status));
    }

    [Fact]
    public async Task MissingCheckIn_ACheckOutWithNoCheckIn()
    {
        AssignSchedule(StandardDay);
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var exception = Assert.Single(db.AttendanceExceptions);
        Assert.Equal((AttendanceExceptionType.MissingPunch, NormalizedEventType.CheckIn, AttendanceExceptionStatus.Open), (exception.ExceptionType, exception.MissingEventType, exception.Status));
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Null(record.ActualCheckIn);
        Assert.Equal(AttendanceRecordStatus.PendingReview, record.Status);
    }

    [Fact]
    public async Task MissingCheckOut_IsNotRaisedBeforeTheGraceWindowHasElapsed()
    {
        AssignSchedule(StandardDay);
        AddFullStandardDay(withoutCheckOut: true);

        // The shift ended at 17:00; the grace runs to 19:00.
        Clock.Local = Monday.At(18, 59);
        var result = await ProcessAsync(Monday);

        Assert.Equal(0, result.ExceptionsRaised);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceExceptions);

        // Still reviewed: the day is incomplete, with or without an exception.
        Assert.Equal(AttendanceRecordStatus.PendingReview, Assert.Single(db.AttendanceRecords).Status);
        Assert.Equal(0, Assert.Single(db.AttendanceDaySummaries).ExceptionCount);
    }

    [Fact]
    public async Task MissingCheckOut_IsRaisedOnceTheGraceWindowHasElapsed_AndOnlyOnce()
    {
        AssignSchedule(StandardDay);
        AddFullStandardDay(withoutCheckOut: true);

        Clock.Local = DueFrom;
        var first = await ProcessAsync(Monday);
        var again = await ProcessAsync(Monday);

        Assert.Equal(1, first.ExceptionsRaised);
        Assert.Equal(0, again.ExceptionsRaised);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var exception = Assert.Single(db.AttendanceExceptions);
        Assert.Equal((AttendanceExceptionType.MissingPunch, NormalizedEventType.CheckOut, AttendanceExceptionStatus.Open), (exception.ExceptionType, exception.MissingEventType, exception.Status));
        Assert.Equal(DueFrom, exception.DetectedAt);
    }

    [Fact]
    public async Task MissingCheckOut_UsesTheGraceOfThePolicy()
    {
        AssignSchedule(StandardDay);
        AddDefaultPolicy(p => p.MissingPunchGraceMinutes = 0);
        AddFullStandardDay(withoutCheckOut: true);

        Clock.Local = Monday.At(17, 0);
        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(NormalizedEventType.CheckOut, Assert.Single(db.AttendanceExceptions).MissingEventType);
    }

    [Fact]
    public async Task MissingBreakIn_ABreakOutWithNoBreakIn()
    {
        AssignSchedule(StandardDay);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var exception = Assert.Single(db.AttendanceExceptions);
        Assert.Equal((AttendanceExceptionType.MissingPunch, NormalizedEventType.BreakIn), (exception.ExceptionType, exception.MissingEventType));
        Assert.Equal(AttendanceRecordStatus.PendingReview, Assert.Single(db.AttendanceRecords).Status);
    }

    [Fact]
    public async Task MissingBreakIn_WhileTheBreakMayStillBeRunning_WaitsForTheGrace()
    {
        AssignSchedule(StandardDay);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));

        Clock.Local = Monday.At(12, 30);
        await ProcessAsync(Monday);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Empty(db.AttendanceExceptions);
        }

        Clock.Local = DueFrom;
        await ProcessAsync(Monday);
        Assert.Equal([NormalizedEventType.CheckOut, NormalizedEventType.BreakIn], MissingPunchTypes());
    }

    [Fact]
    public async Task InvalidSequence_ACheckOutBeforeTheCheckIn()
    {
        AssignSchedule(StandardDay);
        AddPunch(NormalizedEventType.CheckOut, Monday.At(7, 30));
        AddFullStandardDay();

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var exception = Assert.Single(db.AttendanceExceptions);
        Assert.Equal((AttendanceExceptionType.InvalidSequence, AttendanceExceptionStatus.Open), (exception.ExceptionType, exception.Status));
        Assert.Null(exception.MissingEventType);
        var stray = db.NormalizedAttendanceTransactions.Single(n => n.EventTimestampLocal == Monday.At(7, 30));
        Assert.Equal(stray.Id, exception.RelatedNormalizedTransactionId);

        // The rest of the day is a whole, valid day, but the exception keeps it from being approved.
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal(TimeSpan.FromHours(8), record.WorkingHours);
        Assert.Equal(AttendanceRecordStatus.PendingReview, record.Status);
    }

    [Fact]
    public async Task InvalidSequence_TwoCheckInsInsideTheDuplicateWindow_AreOnePunch()
    {
        AssignSchedule(StandardDay);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0).AddSeconds(90));
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));

        var result = await ProcessAsync(Monday);

        Assert.Equal(0, result.ExceptionsRaised);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceExceptions);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((TimeSpan.FromHours(8), AttendanceRecordStatus.Approved), (record.WorkingHours, record.Status));
    }

    [Fact]
    public async Task InvalidSequence_TwoCheckInsOutsideTheDuplicateWindow()
    {
        AssignSchedule(StandardDay);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 10));
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var exception = Assert.Single(db.AttendanceExceptions);
        Assert.Equal(AttendanceExceptionType.InvalidSequence, exception.ExceptionType);

        // The second check-in is the one out of sequence.
        var second = db.NormalizedAttendanceTransactions.Single(n => n.EventTimestampLocal == Monday.At(8, 10));
        Assert.Equal(second.Id, exception.RelatedNormalizedTransactionId);
        Assert.Equal(AttendanceRecordStatus.PendingReview, Assert.Single(db.AttendanceRecords).Status);
    }

    [Fact]
    public async Task InvalidSequence_ABreakInWithNoBreakOut_OnAShiftWithoutABreak()
    {
        AssignSchedule(new ShiftSpec("08:00", "17:00"));
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceExceptionType.InvalidSequence, Assert.Single(db.AttendanceExceptions).ExceptionType);
    }

    [Fact]
    public async Task MissingBreakPunches_OnAShiftWithABreak_WorkedWithoutAnyBreakPunch()
    {
        AssignSchedule(StandardDay);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));

        await ProcessAsync(Monday);

        Assert.Equal([NormalizedEventType.BreakOut, NormalizedEventType.BreakIn], MissingPunchTypes());
    }

    [Fact]
    public async Task ADayOff_WithNoPunches_RaisesNoException_EvenLongAfterwards()
    {
        AssignSchedule(StandardDay);
        var tuesday = Monday.AddDays(1);
        Clock.Local = tuesday.AddDays(30);

        var result = await ProcessAsync(tuesday);

        Assert.Equal(new DayProcessingResult(0, 0, 0), result);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceExceptions);
        Assert.Empty(db.AttendanceRecords);
        Assert.Empty(db.AttendanceDaySummaries);
    }

    [Fact]
    public async Task AnEmployeeWithoutASchedule_WithNoPunches_RaisesNoException()
    {
        Clock.Local = Monday.AddDays(30);

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceExceptions);
    }

    [Fact]
    public async Task AScheduledDay_WithNoPunchesAtAll_IsAMissingCheckInAndCheckOut_OnceTheGraceHasElapsed()
    {
        AssignSchedule(StandardDay);

        Clock.Local = DueFrom.AddMinutes(-1);
        await ProcessAsync(Monday);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Empty(db.AttendanceExceptions);
        }

        Clock.Local = DueFrom;
        await ProcessAsync(Monday);
        Assert.Equal([NormalizedEventType.CheckIn, NormalizedEventType.CheckOut], MissingPunchTypes());
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceRecordStatus.PendingReview, Assert.Single(check.AttendanceRecords).Status);
    }

    [Fact]
    public async Task AnApprovalIsTakenBack_WhenAnOpenExceptionExists_AndTheCalculationIsUnchanged()
    {
        AssignSchedule(StandardDay);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));
        await ProcessAsync(Monday);

        // Someone approves the record while its exception is still open.
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceRecords.Single().Status = AttendanceRecordStatus.Approved;
            db.SaveChanges();
        }

        var again = await ProcessAsync(Monday);

        Assert.Equal(new DayProcessingResult(0, 1, 0), again);
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceRecordStatus.PendingReview, Assert.Single(check.AttendanceRecords).Status);
        Assert.Equal(AttendanceRecordStatus.PendingReview, Assert.Single(check.AttendanceDaySummaries).Status);
    }

    [Fact]
    public async Task AnOpenException_IsAutoResolved_WhenTheMissingPunchArrivesLate()
    {
        AssignSchedule(StandardDay);
        AddFullStandardDay(withoutCheckOut: true);
        Clock.Local = DueFrom;
        await ProcessAsync(Monday);

        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 5));
        Clock.Local = DueFrom.AddHours(1);
        var result = await ProcessAsync(Monday);

        Assert.Equal((0, 1), (result.ExceptionsRaised, result.ExceptionsResolved));
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var exception = Assert.Single(db.AttendanceExceptions);
        Assert.Equal(AttendanceExceptionStatus.Closed, exception.Status);
        Assert.Equal(AttendanceExceptionResolutionMethod.AutoResolved, exception.ResolutionMethod);
        Assert.Equal(DueFrom.AddHours(1), exception.ResolvedAt);

        var current = db.AttendanceRecords.Single(r => r.IsCurrent);
        Assert.Equal(AttendanceRecordStatus.Approved, current.Status);
        Assert.Equal(0, Assert.Single(db.AttendanceDaySummaries).ExceptionCount);
    }

    [Fact]
    public async Task APendingCorrectionException_IsLeftAlone_WhateverReprocessingFinds()
    {
        AssignSchedule(StandardDay);
        AddFullStandardDay(withoutCheckOut: true);
        Clock.Local = DueFrom;
        await ProcessAsync(Monday);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceExceptions.Single().Status = AttendanceExceptionStatus.PendingCorrection;
            db.SaveChanges();
        }

        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 5));
        var result = await ProcessAsync(Monday);

        Assert.Equal((0, 0), (result.ExceptionsRaised, result.ExceptionsResolved));
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceExceptionStatus.PendingCorrection, Assert.Single(check.AttendanceExceptions).Status);

        // Not Approved while a correction for it is pending.
        Assert.Equal(AttendanceRecordStatus.PendingReview, check.AttendanceRecords.Single(r => r.IsCurrent).Status);
    }

    /// <summary>The punches the stored missing-punch exceptions say are absent, in <see cref="NormalizedEventType"/> order.</summary>
    private List<NormalizedEventType> MissingPunchTypes()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return db.AttendanceExceptions
            .Where(e => e.ExceptionType == AttendanceExceptionType.MissingPunch)
            .AsEnumerable()
            .Select(e => e.MissingEventType!.Value)
            .Order()
            .ToList();
    }

    private void AddFullStandardDay(bool withoutCheckOut = false)
    {
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        if (!withoutCheckOut)
        {
            AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));
        }
    }
}
