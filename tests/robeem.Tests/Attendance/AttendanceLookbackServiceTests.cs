using Microsoft.Extensions.Logging.Abstractions;
using robeem.Application.Attendance;
using robeem.Application.HR.Absence;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// The nightly look back: which devices went quiet, which scheduled days nobody punched on (the engine raises the no-show), days still
/// without a check-out, and days that a leave or holiday entered since changes. The clock is the night of Tuesday 6 October 2026 at 03:00, so
/// the window ends on Monday 5 October; the schedule is a Monday-only 08:00-17:00 shift with a break, and a second employee works it too.
/// </summary>
public sealed class AttendanceLookbackServiceTests : AttendanceEngineTestBase
{
    private static readonly DateTime Tuesday = Monday.AddDays(1);
    private readonly int _bob;

    public AttendanceLookbackServiceTests()
    {
        AssignSchedule(StandardDay);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var bob = new Profile { EmployeeNumber = "E-2", FirstName = "Bob", LastName = "Baker", Title = "Bob Baker" };
        db.Add(bob);
        db.SaveChanges();
        var assignment = db.TimeScheduleEmployees.Single(a => a.EmployeeId == EmployeeId);
        db.TimeScheduleEmployees.Add(new TimeScheduleEmployee { TimeScheduleId = assignment.TimeScheduleId, TeamId = assignment.TeamId, EmployeeId = bob.Id });

        // Both were hired long before the window (a new profile is hired today by default).
        foreach (var profile in db.Profiles)
        {
            profile.HireDate = new DateTime(2020, 1, 1);
        }

        db.SaveChanges();
        _bob = bob.Id;
        Clock.Local = Tuesday.At(3, 0);
    }

    [Fact]
    public async Task AScheduledDayNobodyPunchedFor_IsHandedToTheEngine_AndBecomesANoShow_OnlyOnDatesWhenSomebodyPunched()
    {
        // Bob worked the Monday that just ended; nobody worked the Mondays before, so the integration may have been down.
        BobWorks(Monday);
        Clock.Local = Tuesday.At(20, 0);

        var result = await RunAsync();

        var noShow = Assert.Single(Exceptions());
        Assert.Equal((EmployeeId, Monday, AttendanceExceptionType.NoShow), (noShow.EmployeeId, noShow.AttendanceDate, noShow.ExceptionType));
        Assert.Equal((Monday.AddDays(-28), Monday.AddDays(-21), Monday.AddDays(-14), Monday.AddDays(-7)), (result.DatesWithoutData[0], result.DatesWithoutData[1], result.DatesWithoutData[2], result.DatesWithoutData[3]));
        Assert.Equal(4, result.DatesWithoutData.Count);

        // Ada's missing day, and Bob's day, which nobody had calculated yet.
        Assert.Equal((2, 0), (result.DaysCalculated, result.DaysFailed));
    }

    [Fact]
    public async Task ARunIsRepeatable_AndANoShowThatWasRaisedIsNotRaisedAgain()
    {
        BobWorks(Monday);
        Clock.Local = Tuesday.At(20, 0);

        await RunAsync();
        var second = await RunAsync();

        Assert.Single(Exceptions());
        Assert.Equal(0, second.DaysCalculated);
    }

    [Fact]
    public async Task AShiftNotDueYetWhenTheJobRuns_IsLookedAtAgainOnTheNextRun_NotLostBecauseItWasCalculatedEarly()
    {
        ReplaceShift("22:00", "06:00");
        BobWorksTheNight(Monday);

        // 03:00 on Tuesday: the shift that began on Monday night ends at 06:00 and is not due until 08:00.
        await RunAsync();
        Assert.Empty(Exceptions());

        Clock.Local = Tuesday.At(8, 0);
        await RunAsync();

        Assert.Equal(AttendanceExceptionType.NoShow, Assert.Single(Exceptions()).ExceptionType);
    }

    [Fact]
    public async Task ADayCoveredByAHolidayOrLeave_IsNotHandedToTheEngine_AndAHourlyEmployeeNeverIs()
    {
        BobWorks(Monday);
        Clock.Local = Tuesday.At(20, 0);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.PublicHolidays.Add(new PublicHoliday { Date = Monday, Title = "Labour Day" });
            db.SaveChanges();
        }

        var result = await RunAsync();

        Assert.Empty(Exceptions());
        Assert.Equal(0, result.DaysCalculated);

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.PublicHolidays.RemoveRange(db.PublicHolidays);
            db.Profiles.Single(p => p.Id == EmployeeId).PayBasis = PayBasis.Hourly;
            db.SaveChanges();
        }

        await RunAsync();
        Assert.Empty(Exceptions());
    }

    [Fact]
    public async Task AnEmployeeNotYetHired_OrAlreadyTerminated_IsNotMissing()
    {
        BobWorks(Monday);
        Clock.Local = Tuesday.At(20, 0);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.Profiles.Single(p => p.Id == EmployeeId).HireDate = Monday.AddDays(1);
            db.SaveChanges();
        }

        await RunAsync();
        Assert.Empty(Exceptions());

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var ada = db.Profiles.Single(p => p.Id == EmployeeId);
            (ada.HireDate, ada.TerminationDate) = (Monday.AddDays(-100), Monday.AddDays(-1));
            db.SaveChanges();
        }

        await RunAsync();
        Assert.Empty(Exceptions());
    }

    [Fact]
    public async Task AHolidayEnteredAfterTheNoShow_ClosesIt_OnTheNextRun_AndRemovingItBringsTheNoShowBack()
    {
        BobWorks(Monday);
        Clock.Local = Tuesday.At(20, 0);
        await RunAsync();
        Assert.Equal(AttendanceExceptionStatus.Open, Assert.Single(Exceptions()).Status);

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.PublicHolidays.Add(new PublicHoliday { Date = Monday, Title = "Labour Day" });
            db.SaveChanges();
        }

        await RunAsync();
        Assert.Equal((AttendanceExceptionStatus.Closed, AttendanceExceptionResolutionMethod.ExcusedAbsence), (Exceptions()[0].Status, Exceptions()[0].ResolutionMethod!.Value));

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.PublicHolidays.RemoveRange(db.PublicHolidays);
            db.SaveChanges();
        }

        await RunAsync();
        Assert.Equal([AttendanceExceptionStatus.Closed, AttendanceExceptionStatus.Open], Exceptions().Select(e => e.Status));
    }

    [Fact]
    public async Task ReexamineAbsences_IsForTheLeaveModuleToCall_ForOneEmployeeOrEveryone()
    {
        BobWorks(Monday);
        Clock.Local = Tuesday.At(20, 0);
        await RunAsync();
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.EmployeeLeaves.Add(new EmployeeLeave { EmployeeId = EmployeeId, LeaveType = LeaveType.Sick, StartDate = Monday, EndDate = Monday });
            db.SaveChanges();
        }

        using var scope = Database.CreateContext(TestDatabase.TenantA);
        var other = await Service(scope).ReexamineAbsencesAsync(_bob, Monday, Monday, CancellationToken.None);
        Assert.Equal(AttendanceExceptionStatus.Open, Exceptions()[0].Status);

        var calculated = await Service(scope).ReexamineAbsencesAsync(EmployeeId, Monday, Monday, CancellationToken.None);

        Assert.Equal((0, 1), (other, calculated));
        Assert.Equal(AttendanceExceptionResolutionMethod.ExcusedAbsence, Exceptions()[0].ResolutionMethod);
    }

    [Fact]
    public async Task LeaveEnteredAfterADayWasCalculated_IsPickedUp_SoLatenessItCoveredGoesAway()
    {
        AddPunchFor(EmployeeId, NormalizedEventType.CheckIn, Monday.At(9, 30));
        AddPunchFor(EmployeeId, NormalizedEventType.BreakOut, Monday.At(12, 0));
        AddPunchFor(EmployeeId, NormalizedEventType.BreakIn, Monday.At(13, 0));
        AddPunchFor(EmployeeId, NormalizedEventType.CheckOut, Monday.At(17, 0));
        Clock.Local = Tuesday.At(20, 0);
        await ProcessAsync(Monday);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Equal(90, Assert.Single(db.AttendanceRecords).LateMinutes);
            db.EmployeeLeaves.Add(new EmployeeLeave { EmployeeId = EmployeeId, LeaveType = LeaveType.Other, StartDate = Monday, EndDate = Monday, IsPartDay = true, StartTime = "08:00", EndTime = "09:30" });
            db.SaveChanges();
        }

        var result = await RunAsync();

        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.True(result.DaysCalculated >= 1);
        Assert.Equal([(1, 90, 0), (2, 0, 90)], check.AttendanceRecords.Where(r => r.EmployeeId == EmployeeId).OrderBy(r => r.Version).AsEnumerable().Select(r => (r.Version, r.LateMinutes, r.ExcusedMinutes)));
    }

    [Fact]
    public async Task ACheckInWithNoCheckOut_IsLookedAtAgain_SoTheMissingCheckOutIsRaisedOnceDue()
    {
        // Calculated while the shift was still running: nothing could be raised, and no later punch arrives to calculate it again.
        Clock.Local = Monday.At(12, 0);
        AddPunchFor(EmployeeId, NormalizedEventType.CheckIn, Monday.At(8, 0));
        BobWorks(Monday);
        await ProcessAsync(Monday);
        Assert.Empty(Exceptions());

        Clock.Local = Tuesday.At(3, 0);
        await RunAsync();

        // The missing check-out is raised for Ada (and, since nothing was punched for the break, its two punches with it).
        Assert.Contains(Exceptions(), e => e.EmployeeId == EmployeeId && e.MissingEventType == NormalizedEventType.CheckOut);
        Assert.DoesNotContain(Exceptions(), e => e.EmployeeId == _bob);
    }

    [Fact]
    public async Task ARunIsBoundedByTheCap_AndTheRestWaitsForTheNextNight()
    {
        BobWorks(Monday);
        BobWorks(Monday.AddDays(-7));
        Clock.Local = Tuesday.At(20, 0);

        // Four employee-days wait (Ada and Bob, on two Mondays), two at a time, the older date first.
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var first = await Service(db).RunAsync(30, 2, CancellationToken.None);
        var second = await Service(db).RunAsync(30, 2, CancellationToken.None);
        var third = await Service(db).RunAsync(30, 2, CancellationToken.None);

        Assert.Equal((2, 2), (first.DaysCalculated, first.DaysDeferred));
        Assert.Equal((2, 0), (second.DaysCalculated, second.DaysDeferred));
        Assert.Equal(0, third.DaysCalculated);
        Assert.Equal([Monday.AddDays(-7), Monday], Exceptions().Select(e => e.AttendanceDate).Order());
    }

    [Fact]
    public async Task ADeviceOnALiveConnectionWithNoPunch_IsSilent_AndOneThatSentSomethingOrIsUploadedByHandIsNot()
    {
        AddDevice("Gate A", AttendanceTransport.RestApi, sendsPunches: true);
        AddDevice("Gate B", AttendanceTransport.Webhook, sendsPunches: false);
        AddDevice("Office scanner", AttendanceTransport.FileUpload, sendsPunches: false);

        var result = await RunAsync();

        Assert.Equal(2, result.DevicesChecked);
        var silent = Assert.Single(result.SilentDevices);
        Assert.Equal(("Gate B", "HQ"), (silent.Name, silent.Integration));
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Contains(db.AttendanceAuditLogs, a => a.Action == AttendanceAuditActions.AttendanceDataRead && a.Details!.Contains("for the look-back"));

        // The device's integration is not touched: its health is the sync job's to write.
        Assert.All(db.AttendanceIntegrations, i => Assert.Equal(AttendanceIntegrationHealth.Unknown, i.HealthStatus));
    }

    private AttendanceLookbackService Service(RobeemDbContext db) =>
        new(db, CreateEngine(db), new ScheduleResolutionService(db), new HrAbsenceCalendar(db), CreateAudit(db), Clock, new RuleSetProvider(db), NullLogger<AttendanceLookbackService>.Instance);

    private async Task<LookbackResult> RunAsync()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await Service(db).RunAsync(30, 2000, CancellationToken.None);
    }

    private List<AttendanceException> Exceptions()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return db.AttendanceExceptions.OrderBy(e => e.Id).ToList();
    }

    /// <summary>Bob works the whole day, break included: a day with data, so a missing Ada is real, and Bob raises nothing himself.</summary>
    private void BobWorks(DateTime date)
    {
        AddPunchFor(_bob, NormalizedEventType.CheckIn, date.At(8, 0));
        AddPunchFor(_bob, NormalizedEventType.BreakOut, date.At(12, 0));
        AddPunchFor(_bob, NormalizedEventType.BreakIn, date.At(13, 0));
        AddPunchFor(_bob, NormalizedEventType.CheckOut, date.At(17, 0));
    }

    /// <summary>Bob works the night shift (22:00 to 06:00) that begins on the date.</summary>
    private void BobWorksTheNight(DateTime date)
    {
        AddPunchFor(_bob, NormalizedEventType.CheckIn, date.At(22, 0));
        AddPunchFor(_bob, NormalizedEventType.CheckOut, date.AddDays(1).At(6, 0));
    }

    private void ReplaceShift(string start, string end)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var shift = db.TimeScheduleShifts.Single();
        (shift.StartTime, shift.EndTime, shift.BreakTime, shift.BreakStartTime, shift.BreakEndTime) = (start, end, false, "00:00", "00:00");
        db.SaveChanges();
    }

    private void AddPunchFor(int employeeId, NormalizedEventType type, DateTime local)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var device = db.AttendanceDevices.OrderBy(d => d.Id).First();
        var raw = db.RawAttendanceTransactions.OrderBy(r => r.Id).First();
        var utc = new DateTimeOffset(local, TimeSpan.Zero);
        db.NormalizedAttendanceTransactions.Add(new NormalizedAttendanceTransaction
        {
            EmployeeId = employeeId,
            DeviceId = device.Id,
            IntegrationId = device.IntegrationId,
            RawTransactionId = raw.Id,
            AttendanceDate = local.Date,
            EventTimestampUtc = utc,
            EventTimestampLocal = local,
            TimeZoneId = "UTC",
            NormalizedEventType = type,
            ConfidenceStatus = EventConfidenceStatus.Certain,
            DuplicateBucketTimestampUtc = utc,
        });
        db.SaveChanges();
    }

    /// <summary>A second device of the integration, on a connection of the given transport, with or without a punch received through it.</summary>
    private void AddDevice(string name, AttendanceTransport transport, bool sendsPunches)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var integration = db.AttendanceIntegrations.Single();
        integration.DisplayName = "HQ";
        var connection = new AttendanceConnection { Integration = integration, Name = $"{name} connection", Transport = transport, IsActive = true };
        var device = new AttendanceDevice { Integration = integration, ExternalDeviceId = name, Name = name, TimeZoneId = "UTC", Connection = connection };
        db.AddRange(connection, device);
        db.SaveChanges();

        if (sendsPunches)
        {
            db.RawAttendanceTransactions.Add(new RawAttendanceTransaction
            {
                IntegrationId = integration.Id,
                DeviceId = device.Id,
                ImportBatch = new AttendanceProcessingBatch { IntegrationId = integration.Id, BatchType = AttendanceBatchType.ManualUpload, StartedAt = Monday },
                TransactionTimestampRaw = "-",
                RawPayload = "{}",
                SourceSystem = "generic-csv",
                RecordHash = $"hash-{name}",
            });
            db.SaveChanges();
        }
    }
}
