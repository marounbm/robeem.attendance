using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using robeem.Application.Attendance;
using robeem.Application.HR.Holidays;
using robeem.Application.HR.Leaves;
using robeem.Contracts.HR.Holidays;
using robeem.Contracts.HR.Leaves;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// Calculating the attendance already held again from a date, after the policy, the rules, a holiday or a leave changed: only the days that come
/// out different are rewritten, exceptions whose problem is gone close, a stricter policy raises its exceptions, and a closed pay period is left
/// alone. The shift is Monday-only 08:00-17:00 with no break; the clock is after the third Monday.
/// </summary>
public sealed class AttendanceReprocessingServiceTests : AttendanceEngineTestBase
{
    private static readonly DateTime SecondMonday = Monday.AddDays(7);
    private static readonly DateTime ThirdMonday = Monday.AddDays(14);

    public AttendanceReprocessingServiceTests()
    {
        AssignSchedule(new ShiftSpec("08:00", "17:00"));
        using var db = Database.CreateContext(TestDatabase.TenantA);
        db.Profiles.Single(p => p.Id == EmployeeId).HireDate = new DateTime(2020, 1, 1);
        db.SaveChanges();

        // A lenient policy: lateness and leaving early of up to three hours are forgiven.
        AddDefaultPolicy(p => (p.LateGraceMinutes, p.EarlyDepartureGraceMinutes) = (180, 180));
        Clock.Local = ThirdMonday.At(20, 0);
    }

    [Fact]
    public async Task AStricterPolicy_RaisesTheExceptionOnTheDayItChanges_AndLeavesTheOtherDaysAsTheyWere()
    {
        await WorkAsync(Monday, "I@09:30,O@17:00");
        await WorkAsync(SecondMonday, "I@08:00,O@17:00");
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Empty(db.AttendanceExceptions);
            Assert.All(db.AttendanceRecords, r => Assert.Equal(AttendanceRecordStatus.Approved, r.Status));
        }

        ChangePolicy(p => (p.LateGraceMinutes, p.EarlyDepartureGraceMinutes) = (10, 10));
        var result = await ReprocessAsync(Monday);

        Assert.Equal((2, 1, 1), (result.DaysExamined, result.DaysChanged, result.ExceptionsRaised));
        using var check = Database.CreateContext(TestDatabase.TenantA);
        var exception = Assert.Single(check.AttendanceExceptions);
        Assert.Equal((AttendanceExceptionType.ShortHours, Monday, AttendanceExceptionStatus.Open), (exception.ExceptionType, exception.AttendanceDate, exception.Status));

        // The late day is no longer approved; the day that came out the same keeps its one record.
        Assert.Equal(AttendanceRecordStatus.PendingReview, Assert.Single(check.AttendanceRecords, r => r.AttendanceDate == Monday && r.IsCurrent).Status);
        var untouched = Assert.Single(check.AttendanceRecords, r => r.AttendanceDate == SecondMonday);
        Assert.Equal((1, AttendanceRecordStatus.Approved), (untouched.Version, untouched.Status));
        Assert.Contains(check.AttendanceAuditLogs, a => a.Action == AttendanceAuditActions.DayReprocessed && a.Details!.StartsWith("Calculated again from 2026-10-05", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APolicyThatBecameLenient_ClosesTheOpenExceptionItNoLongerRaises_ByItself()
    {
        ChangePolicy(p => (p.LateGraceMinutes, p.EarlyDepartureGraceMinutes) = (10, 10));
        await WorkAsync(Monday, "I@09:30,O@17:00");
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Equal(AttendanceExceptionStatus.Open, Assert.Single(db.AttendanceExceptions).Status);
        }

        ChangePolicy(p => (p.LateGraceMinutes, p.EarlyDepartureGraceMinutes) = (180, 180));
        var result = await ReprocessAsync(Monday);

        Assert.Equal(1, result.ExceptionsResolved);
        using var check = Database.CreateContext(TestDatabase.TenantA);
        var exception = Assert.Single(check.AttendanceExceptions);
        Assert.Equal((AttendanceExceptionStatus.Closed, AttendanceExceptionResolutionMethod.AutoResolved), (exception.Status, exception.ResolutionMethod!.Value));
        Assert.Equal(AttendanceRecordStatus.Approved, Assert.Single(check.AttendanceRecords, r => r.IsCurrent).Status);
    }

    [Fact]
    public async Task APayPeriodClosedForOneEmployee_HoldsBackOnlyThatEmployeesDays()
    {
        await WorkAsync(Monday, "I@09:30,O@17:00");
        int other;
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var grace = new Profile { EmployeeNumber = "E-2", FirstName = "Grace", LastName = "Hopper", Title = "Grace Hopper" };
            db.Profiles.Add(grace);
            db.SaveChanges();
            other = grace.Id;

            // Closed for Grace: Ada's day of the same dates is still hers to be calculated again.
            db.PayPeriods.Add(new PayPeriod { EmployeeId = other, StartDate = Monday, EndDate = Monday.AddDays(6), IsClosed = true });
            db.SaveChanges();
        }

        ChangePolicy(p => (p.LateGraceMinutes, p.EarlyDepartureGraceMinutes) = (10, 10));
        var notHeld = await ReprocessAsync(Monday);

        Assert.Equal((1, 0), (notHeld.DaysExamined, notHeld.DaysInClosedPeriods));

        // Closed for Ada herself, it holds her day back.
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.PayPeriods.Add(new PayPeriod { EmployeeId = EmployeeId, StartDate = Monday, EndDate = Monday.AddDays(6), IsClosed = true });
            db.SaveChanges();
        }

        var held = await ReprocessAsync(Monday);

        Assert.Equal((0, 1), (held.DaysExamined, held.DaysInClosedPeriods));
    }

    [Fact]
    public async Task ADayInAClosedPayPeriod_IsNeverCalculatedAgain()
    {
        await WorkAsync(Monday, "I@09:30,O@17:00");
        await WorkAsync(SecondMonday, "I@09:30,O@17:00");
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.PayPeriods.Add(new PayPeriod { StartDate = Monday, EndDate = Monday.AddDays(6), IsClosed = true });
            db.SaveChanges();
        }

        ChangePolicy(p => (p.LateGraceMinutes, p.EarlyDepartureGraceMinutes) = (10, 10));
        var result = await ReprocessAsync(Monday);

        Assert.Equal((1, 1), (result.DaysExamined, result.DaysInClosedPeriods));
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(SecondMonday, Assert.Single(check.AttendanceExceptions).AttendanceDate);
    }

    [Fact]
    public async Task ARunStopsAtItsLimit_SaysWhereToCarryOn_AndEveryDayIsDoneOnce()
    {
        await WorkAsync(Monday, "I@08:00,O@17:00");
        await WorkAsync(SecondMonday, "I@08:00,O@17:00");
        await WorkAsync(ThirdMonday, "I@08:00,O@17:00");

        var first = await ReprocessAsync(Monday, maxDays: 1);
        var second = await ReprocessAsync(first.NextFrom!.Value, maxDays: 5);

        Assert.Equal((1, SecondMonday), (first.DaysExamined, first.NextFrom));
        Assert.Equal((2, (DateTime?)null), (second.DaysExamined, second.NextFrom));
    }

    [Fact]
    public async Task ADateIsFinishedWhateverTheLimit_SoACarryOnNeverStartsTheSameDateOver()
    {
        var bob = AddEmployee();
        await WorkAsync(Monday, "I@08:00,O@17:00");
        await WorkAsync(SecondMonday, "I@08:00,O@17:00");
        await WorkAsync(Monday, "I@08:00,O@17:00", bob);

        // The limit is one, but Monday has two employees: both are done, and the carry-on is the next date.
        var result = await ReprocessAsync(Monday, maxDays: 1);

        Assert.Equal((2, SecondMonday), (result.DaysExamined, result.NextFrom));

        // One employee only: Bob's other days are not looked at.
        var adaOnly = await ReprocessAsync(Monday, employeeId: EmployeeId);
        Assert.Equal(2, adaOnly.DaysExamined);
    }

    [Fact]
    public async Task AHolidayEnteredAfterANoShow_ClosesIt_WhenTheDaysAreCalculatedAgain()
    {
        // Nobody punched on Monday and the shift is long over: a no-show.
        await ProcessAsync(Monday);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Equal(AttendanceExceptionType.NoShow, Assert.Single(db.AttendanceExceptions).ExceptionType);
            db.PublicHolidays.Add(new PublicHoliday { Date = Monday, Title = "Labour Day" });
            db.SaveChanges();
        }

        var result = await ReprocessAsync(Monday);

        Assert.Equal(1, result.ExceptionsResolved);
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceExceptionResolutionMethod.ExcusedAbsence, Assert.Single(check.AttendanceExceptions).ResolutionMethod);
    }

    [Fact]
    public async Task ALeaveSavedForADayWithANoShow_SettlesItAtOnce_WithoutAnyoneCalculatingAgain()
    {
        // Nobody punched on Monday and the shift is long over: a no-show.
        await ProcessAsync(Monday);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Equal(AttendanceExceptionStatus.Open, Assert.Single(db.AttendanceExceptions).Status);
        }

        // A leave on another day changes nothing for it.
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            await Leaves(db).CreateAsync(LeaveOn(SecondMonday), CancellationToken.None);
        }

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Equal(AttendanceExceptionStatus.Open, Assert.Single(db.AttendanceExceptions).Status);
        }

        // A leave on the day closes it, as an excused absence.
        int leaveId;
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            leaveId = (await Leaves(db).CreateAsync(LeaveOn(Monday), CancellationToken.None)).Id!.Value;
        }

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var exception = Assert.Single(db.AttendanceExceptions);
            Assert.Equal((AttendanceExceptionStatus.Closed, AttendanceExceptionResolutionMethod.ExcusedAbsence), (exception.Status, exception.ResolutionMethod!.Value));
            Assert.Equal(0, Assert.Single(db.AttendanceDaySummaries).ExceptionCount);
        }

        // Taking the leave away brings the no-show back.
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            await Leaves(db).DeleteAsync(leaveId, CancellationToken.None);
        }

        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal([AttendanceExceptionStatus.Closed, AttendanceExceptionStatus.Open], check.AttendanceExceptions.OrderBy(e => e.Id).Select(e => e.Status));
    }

    [Fact]
    public async Task AHolidaySaved_SettlesTheNoShowsOfEveryone_AndMovingItFreesTheOldDate()
    {
        await ProcessAsync(Monday);
        await ProcessAsync(SecondMonday);

        int holidayId;
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            holidayId = (await Holidays(db).CreateAsync(new HolidayInput { Date = Monday, Name = "Labour Day" }, CancellationToken.None)).Id!.Value;
        }

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Equal([AttendanceExceptionStatus.Closed, AttendanceExceptionStatus.Open], db.AttendanceExceptions.OrderBy(e => e.AttendanceDate).Select(e => e.Status));

            // Moved to the other Monday: the first is a working day again, the second is excused.
            await Holidays(db).UpdateAsync(holidayId, new HolidayInput { Date = SecondMonday, Name = "Labour Day" }, CancellationToken.None);
        }

        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(
            [(Monday, AttendanceExceptionStatus.Closed), (Monday, AttendanceExceptionStatus.Open), (SecondMonday, AttendanceExceptionStatus.Closed)],
            check.AttendanceExceptions.OrderBy(e => e.AttendanceDate).ThenBy(e => e.Id).AsEnumerable().Select(e => (e.AttendanceDate, e.Status)));
    }

    [Fact]
    public async Task ALeaveIsSaved_EvenWhenCalculatingTheDaysAgainFails()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var leaves = new LeaveService(db, new AttendanceAbsenceChangeNotifier(new ThrowingReprocessing(), NullLogger<AttendanceAbsenceChangeNotifier>.Instance));

        var result = await leaves.CreateAsync(LeaveOn(Monday), CancellationToken.None);

        Assert.True(result.Succeeded);
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Single(check.EmployeeLeaves);
    }

    [Fact]
    public async Task ARangeStopsAtItsLastDay_AndLeavesTheLaterDaysAlone()
    {
        await WorkAsync(Monday, "I@09:30,O@17:00");
        await WorkAsync(SecondMonday, "I@09:30,O@17:00");
        ChangePolicy(p => (p.LateGraceMinutes, p.EarlyDepartureGraceMinutes) = (10, 10));

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var result = await Service(db).ReprocessRangeAsync(Monday, Monday.AddDays(3), null, 2000, CancellationToken.None);
            Assert.Equal((Monday.AddDays(3), 1), (result.To, result.DaysExamined));
        }

        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(Monday, Assert.Single(check.AttendanceExceptions).AttendanceDate);
    }

    [Fact]
    public async Task OvertimeTheEngineApproved_NeedsAReviewOnceThePolicyAsksForPreApproval_ButAReviewersApprovalStays()
    {
        await WorkAsync(Monday, "I@08:00,O@19:00");
        await WorkAsync(SecondMonday, "I@08:00,O@19:00");
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            // A reviewer approved the second Monday's overtime by hand.
            var record = db.AttendanceRecords.Single(r => r.AttendanceDate == SecondMonday);
            Assert.Equal((AttendanceRecordStatus.Approved, 120), (record.Status, record.OvertimeMinutes));
            db.AttendanceApprovals.Add(new AttendanceApproval
            {
                Type = AttendanceApprovalType.Review,
                Status = AttendanceApprovalStatus.Approved,
                EmployeeId = EmployeeId,
                AttendanceDate = SecondMonday,
                Workflow = ApprovalWorkflow.ManagerOrHr,
                RequestedAt = SecondMonday,
                AttendanceRecordId = record.Id,
            });
            db.SaveChanges();
        }

        ChangePolicy(p => p.RequiresOvertimePreApproval = true);
        var result = await ReprocessAsync(Monday);

        Assert.Equal((2, 1, 1), (result.DaysExamined, result.DaysChanged, result.RecordsSentToReview));
        using var check = Database.CreateContext(TestDatabase.TenantA);
        var first = Assert.Single(check.AttendanceRecords, r => r.AttendanceDate == Monday);
        Assert.Equal(AttendanceRecordStatus.PendingReview, first.Status);
        Assert.Contains(check.AttendanceApprovals, a => a.AttendanceRecordId == first.Id && a.Status == AttendanceApprovalStatus.Pending);
        Assert.Equal(AttendanceRecordStatus.Approved, Assert.Single(check.AttendanceRecords, r => r.AttendanceDate == SecondMonday).Status);
    }

    [Fact]
    public async Task NothingBeforeTheStartDate_AndNothingAfterToday_IsCalculatedAgain()
    {
        await WorkAsync(Monday, "I@08:00,O@17:00");
        await WorkAsync(SecondMonday, "I@08:00,O@17:00");
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceSettings.Add(new AttendanceSettings { StartDate = SecondMonday });
            db.SaveChanges();
        }

        var result = await ReprocessAsync(Monday);
        var future = await ReprocessAsync(ThirdMonday.AddDays(30));

        Assert.Equal((SecondMonday, 1), (result.From, result.DaysExamined));
        Assert.Equal(0, future.DaysExamined);
    }

    private AttendanceReprocessingService Service(RobeemDbContext db) =>
        new(db, CreateEngine(db), CreateAudit(db), Clock, NullLogger<AttendanceReprocessingService>.Instance);

    /// <summary>The leave service as the host builds it: saving a leave settles the days it covers.</summary>
    private LeaveService Leaves(RobeemDbContext db) => new(db, Notifier(db));

    private HolidayService Holidays(RobeemDbContext db) => new(db, Notifier(db));

    private AttendanceAbsenceChangeNotifier Notifier(RobeemDbContext db) => new(Service(db), NullLogger<AttendanceAbsenceChangeNotifier>.Instance);

    private LeaveInput LeaveOn(DateTime day) => new() { EmployeeId = EmployeeId, LeaveType = nameof(LeaveType.Sick), StartDate = day, EndDate = day };

    private async Task<ReprocessSinceResult> ReprocessAsync(DateTime from, int? employeeId = null, int maxDays = 2000)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await Service(db).ReprocessSinceAsync(from, employeeId, maxDays, CancellationToken.None);
    }

    private void ChangePolicy(Action<AttendancePolicy> change)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        change(db.AttendancePolicies.Single());
        db.SaveChanges();
    }

    /// <summary>Gives the day its punches ("I@09:30,O@17:00": I check-in, O check-out) and calculates it, the way normalization would.</summary>
    private async Task WorkAsync(DateTime date, string punches, int? employeeId = null)
    {
        var id = employeeId ?? EmployeeId;
        foreach (var punch in punches.Split(','))
        {
            var (kind, time) = (punch[0], TimeSpan.Parse(punch[2..], CultureInfo.InvariantCulture));
            AddPunchOf(id, kind == 'I' ? NormalizedEventType.CheckIn : NormalizedEventType.CheckOut, date.Add(time));
        }

        using var db = Database.CreateContext(TestDatabase.TenantA);
        await CreateEngine(db).ProcessEmployeeDayAsync(id, date, CancellationToken.None);
    }

    private int AddEmployee()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var bob = new Profile { EmployeeNumber = "E-2", FirstName = "Bob", LastName = "Baker", Title = "Bob Baker", HireDate = new DateTime(2020, 1, 1) };
        db.Add(bob);
        db.SaveChanges();
        var assignment = db.TimeScheduleEmployees.Single(a => a.EmployeeId == EmployeeId);
        db.TimeScheduleEmployees.Add(new TimeScheduleEmployee { TimeScheduleId = assignment.TimeScheduleId, TeamId = assignment.TeamId, EmployeeId = bob.Id });
        db.SaveChanges();
        return bob.Id;
    }

    private void AddPunchOf(int employeeId, NormalizedEventType type, DateTime local)
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

    private sealed class ThrowingReprocessing : IAttendanceReprocessingService
    {
        public Task<ReprocessSinceResult> ReprocessSinceAsync(DateTime from, int? employeeId, int maxDays, CancellationToken cancellationToken) => throw new InvalidOperationException("boom");

        public Task<ReprocessSinceResult> ReprocessRangeAsync(DateTime from, DateTime to, int? employeeId, int maxDays, CancellationToken cancellationToken) => throw new InvalidOperationException("boom");
    }
}
