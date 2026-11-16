using robeem.DataModel;

namespace robeem.Tests.Attendance;

/// <summary>
/// What a machine puts on a punch decides how its punches are read. A check-out and the next check-in in the middle of a day are read as a break
/// only when the machine sends no break types; a machine that does send them records a break as one, so they are a real exit. A machine that sends
/// no types has its punches typed in order. The shift is 08:00-17:00 with a break from 12:00 to 13:00.
/// </summary>
public sealed class AttendanceDevicePunchTypesTests : AttendanceEngineTestBase
{
    public AttendanceDevicePunchTypesTests()
    {
        AssignSchedule(StandardDay);
        Clock.Local = Monday.At(18, 0);
    }

    [Fact]
    public async Task ACheckOutAndCheckInInTheMiddleOfTheDay_AreTheBreak_OnAMachineThatSendsNoBreakTypes()
    {
        AddDefaultPolicy(p => p.BreakInferenceMode = BreakInferenceMode.FromGap);
        AddInOutDay();

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((Monday.At(12, 20), Monday.At(13, 5), AttendancePunchSource.Inferred), (record.ActualBreakOut, record.ActualBreakIn, record.ActualBreakOutSource));
        Assert.Equal(TimeSpan.FromMinutes(45), record.BreakDuration);
    }

    [Fact]
    public async Task OnAMachineThatSendsBreakTypes_ACheckOutAndCheckIn_AreARealExit_NeverReadAsTheBreak()
    {
        AddDefaultPolicy(p => p.BreakInferenceMode = BreakInferenceMode.FromGap);
        SetDevicePunchTypes(PunchTypeSupport.InOutAndBreak);
        AddInOutDay();

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Null(record.ActualBreakOut);
        Assert.Equal(BreakFindings.NoBreak, record.BreakFindings);
    }

    [Fact]
    public async Task APunchWithNoType_IsTypedInOrder_OnAMachineThatSendsNoTypes_WhateverThePolicySays()
    {
        SetDevicePunchTypes(PunchTypeSupport.None);
        AddPunch(NormalizedEventType.Unknown, Monday.At(8, 0));
        AddPunch(NormalizedEventType.Unknown, Monday.At(17, 0));

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((Monday.At(8, 0), Monday.At(17, 0), AttendancePunchSource.Inferred, AttendancePunchSource.Inferred), (record.ActualCheckIn, record.ActualCheckOut, record.ActualCheckInSource, record.ActualCheckOutSource));
    }

    [Fact]
    public async Task APunchWithNoType_IsLeftForReview_OnAMachineThatSendsTypes_WhenThePolicyDoesNotTypeThem()
    {
        AddPunch(NormalizedEventType.Unknown, Monday.At(8, 0));
        AddPunch(NormalizedEventType.Unknown, Monday.At(17, 0));

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.All(db.AttendanceRecords, r => Assert.Null(r.ActualCheckIn));
    }

    private void AddInOutDay()
    {
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(12, 20));
        AddPunch(NormalizedEventType.CheckIn, Monday.At(13, 5));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));
    }
}
