using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// The break rules: breaks shorter than the policy's minimum are ignored (a toilet visit), and a break that is long, late, early or not
/// taken is recorded on the day and, when the policy says so, raised as an exception that a manager or HR accepts.
/// The shift is 08:00-17:00 with a break from 12:00 to 13:00.
/// </summary>
public sealed class AttendanceBreakRulesTests : AttendanceEngineTestBase
{
    private readonly int _managerId;

    public AttendanceBreakRulesTests()
    {
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var manager = new Profile { EmployeeNumber = "M-1", FirstName = "Grace", LastName = "Hopper", Title = "Grace Hopper" };
            db.Add(manager);
            db.SaveChanges();
            db.Profiles.Single(p => p.Id == EmployeeId).ManagerId = manager.Id;
            db.SaveChanges();
            _managerId = manager.Id;
        }

        AssignSchedule(StandardDay);
        Clock.Local = Monday.At(18, 0);
    }

    [Fact]
    public async Task AShortBreak_IsATrip_NotABreak_ItsTimeCountsAsWork_AndRaisesNothing()
    {
        AddPunches("I@08:00,BO@10:00,BI@10:07,BO@12:00,BI@13:00,O@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((TimeSpan.FromHours(8), TimeSpan.FromHours(1), BreakFindings.None, AttendanceRecordStatus.Approved), (record.WorkingHours, record.BreakDuration, record.BreakFindings, record.Status));
        Assert.Equal(Monday.At(12, 0), record.ActualBreakOut);
        Assert.Empty(db.AttendanceExceptions);
    }

    [Fact]
    public async Task TheMinimumIsThePolicys_ABreakAsLongAsItIsKept()
    {
        AddDefaultPolicy(p => p.IgnoreBreaksShorterThanMinutes = 5);
        AddPunches("I@08:00,BO@10:00,BI@10:07,BO@12:00,BI@13:00,O@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);

        // The first break lasted 7 minutes, over the minimum of 5: it is a break, and the day's first one.
        Assert.Equal((TimeSpan.FromMinutes(7 + 60), Monday.At(10, 0)), (record.BreakDuration, record.ActualBreakOut));
        Assert.True(record.BreakFindings.HasFlag(BreakFindings.EarlyBreak));
    }

    [Fact]
    public async Task ShortBreaksCanBeSwitchedOff()
    {
        AddDefaultPolicy(p => p.IgnoreBreaksShorterThanMinutes = 0);
        AddPunches("I@08:00,BO@12:00,BI@12:03,O@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(TimeSpan.FromMinutes(3), Assert.Single(db.AttendanceRecords).BreakDuration);
    }

    [Fact]
    public async Task OnlyAShortBreak_IsNoBreak_AndTheMissingBreakPunchesAreRaisedByDefault()
    {
        AddPunches("I@08:00,BO@12:00,BI@12:05,O@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((BreakFindings.NoBreak, AttendanceRecordStatus.PendingReview), (record.BreakFindings, record.Status));
        Assert.Equal(
            [NormalizedEventType.BreakOut, NormalizedEventType.BreakIn],
            db.AttendanceExceptions.Where(e => e.ExceptionType == AttendanceExceptionType.MissingPunch).Select(e => e.MissingEventType!.Value).AsEnumerable().Order());
    }

    [Fact]
    public async Task NoBreak_CanBeAllowedByThePolicy_TheDayIsCompleteAndApproved_AndTheFindingIsStillRecorded()
    {
        AddDefaultPolicy(p => p.RaiseExceptionOnNoBreak = false);
        AddPunches("I@08:00,O@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((BreakFindings.NoBreak, AttendanceRecordStatus.Approved), (record.BreakFindings, record.Status));
        Assert.Empty(db.AttendanceExceptions);
    }

    [Theory]
    [InlineData("I@08:00,BO@12:00,BI@13:30,O@17:00", BreakFindings.LongBreak)]
    [InlineData("I@08:00,BO@13:00,BI@14:00,O@17:00", BreakFindings.LateBreak)]
    [InlineData("I@08:00,BO@11:00,BI@12:00,O@17:00", BreakFindings.EarlyBreak)]
    [InlineData("I@08:00,BO@13:00,BI@14:30,O@17:00", BreakFindings.LateBreak | BreakFindings.LongBreak)]
    public async Task ALongLateOrEarlyBreak_IsRecordedOnTheDay_WithoutAnException_WhenThePolicyDoesNotAskForOne(string punches, BreakFindings expected)
    {
        AddPunches(punches);

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((expected, AttendanceRecordStatus.Approved), (record.BreakFindings, record.Status));
        Assert.Empty(db.AttendanceExceptions);
    }

    [Theory]
    [InlineData("I@08:00,BO@12:15,BI@13:20,O@17:00")]
    [InlineData("I@08:00,BO@11:45,BI@12:50,O@17:00")]
    public async Task ABreakInsideTheAllowances_IsAsScheduled(string punches)
    {
        AddPunches(punches);

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(BreakFindings.None, Assert.Single(db.AttendanceRecords).BreakFindings);
    }

    [Theory]
    [InlineData("I@08:00,BO@12:00,BI@13:30,O@17:00", AttendanceExceptionType.LongBreak)]
    [InlineData("I@08:00,BO@13:00,BI@14:00,O@17:00", AttendanceExceptionType.LateBreak)]
    [InlineData("I@08:00,BO@11:00,BI@12:00,O@17:00", AttendanceExceptionType.EarlyBreak)]
    public async Task AFindingRaisesAnException_WhenThePolicyAsksForIt_AndTheDayWaitsForSomeoneToAcceptIt(string punches, AttendanceExceptionType expected)
    {
        AddDefaultPolicy(p => (p.RaiseExceptionOnLongBreak, p.RaiseExceptionOnLateBreak, p.RaiseExceptionOnEarlyBreak) = (true, true, true));
        AddPunches(punches);

        var result = await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var exception = Assert.Single(db.AttendanceExceptions);
        Assert.Equal((expected, AttendanceExceptionStatus.Open, 1), (exception.ExceptionType, exception.Status, result.ExceptionsRaised));
        Assert.NotNull(exception.RelatedNormalizedTransactionId);
        Assert.Equal(AttendanceRecordStatus.PendingReview, Assert.Single(db.AttendanceRecords).Status);
    }

    [Fact]
    public async Task EachFindingIsRaisedOnItsOwn_AccordingToItsOwnSetting()
    {
        AddDefaultPolicy(p => p.RaiseExceptionOnLateBreak = true);
        AddPunches("I@08:00,BO@13:00,BI@14:30,O@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);

        // The break was late and long; only late is raised.
        Assert.Equal(AttendanceExceptionType.LateBreak, Assert.Single(db.AttendanceExceptions).ExceptionType);
        Assert.Equal(BreakFindings.LateBreak | BreakFindings.LongBreak, Assert.Single(db.AttendanceRecords).BreakFindings);
    }

    [Fact]
    public async Task AcceptingABreakException_ClosesItWithTheReason_AndTheDayIsApproved_AndItStaysAcceptedWhenTheDayIsCalculatedAgain()
    {
        AddDefaultPolicy(p => p.RaiseExceptionOnLongBreak = true);
        AddPunches("I@08:00,BO@12:00,BI@13:30,O@17:00");
        await ProcessAsync(Monday);
        var id = ExceptionId();

        var accepted = await AcceptAsync(id, _managerId, asHr: false, "Covered the phones at lunch.");
        await ProcessAsync(Monday);

        Assert.True(accepted.Succeeded);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var exception = Assert.Single(db.AttendanceExceptions);
        Assert.Equal((AttendanceExceptionStatus.Closed, AttendanceExceptionResolutionMethod.Accepted, _managerId, "Covered the phones at lunch."), (exception.Status, exception.ResolutionMethod, exception.ResolvedById, exception.ResolutionNote));
        Assert.Equal(AttendanceRecordStatus.Approved, db.AttendanceRecords.Single(r => r.IsCurrent).Status);
        Assert.Equal(0, Assert.Single(db.AttendanceDaySummaries).ExceptionCount);
        Assert.Contains(db.AttendanceAuditLogs, a => a.Action == AttendanceAuditActions.ExceptionStatusChanged && a.Details!.Contains("accepted by Manager. Reason: Covered the phones at lunch."));
    }

    [Fact]
    public async Task ABreakThatChanges_IsAnotherBreak_AndIsRaisedAgainEvenAfterTheFirstWasAccepted()
    {
        AddDefaultPolicy(p => p.RaiseExceptionOnLongBreak = true);
        AddPunches("I@08:00,BO@12:00,BI@13:30,O@17:00");
        await ProcessAsync(Monday);
        await AcceptAsync(ExceptionId(), _managerId, asHr: false, "Covered the phones at lunch.");

        // A correction of the day brings a different first break (an earlier break-out the clock sent late).
        AddPunch(NormalizedEventType.BreakOut, Monday.At(11, 50));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 40));
        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(2, db.AttendanceExceptions.Count(e => e.ExceptionType == AttendanceExceptionType.LongBreak));
    }

    [Fact]
    public async Task OnlyTheEmployeesManagerOrHr_CanAccept_NeverTheEmployee_AndAReasonIsRequired()
    {
        AddDefaultPolicy(p => p.RaiseExceptionOnLongBreak = true);
        AddPunches("I@08:00,BO@12:00,BI@13:30,O@17:00");
        await ProcessAsync(Monday);
        var id = ExceptionId();
        int strangerId;
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var stranger = new Profile { EmployeeNumber = "S-1", FirstName = "Alan", LastName = "Turing", Title = "Alan Turing" };
            db.Add(stranger);
            db.SaveChanges();
            strangerId = stranger.Id;
        }

        var byEmployee = await AcceptAsync(id, EmployeeId, asHr: false, "Because I said so.");
        var byStranger = await AcceptAsync(id, strangerId, asHr: false, "Not my report.");
        var withoutReason = await AcceptAsync(id, _managerId, asHr: false, "  ");
        var byHr = await AcceptAsync(id, strangerId, asHr: true, "HR agrees.");

        Assert.Contains("own employee", byEmployee.Errors[CorrectionErrorKeys.DecidedBy]);
        Assert.Contains("manager, or HR", byStranger.Errors[CorrectionErrorKeys.DecidedBy]);
        Assert.Contains("reason is required", withoutReason.Errors[nameof(AcceptExceptionRequest.Reason)]);
        Assert.True(byHr.Succeeded);
    }

    [Fact]
    public async Task AMissingPunchIsNotAccepted_ItIsCorrected_AndAnExceptionIsAcceptedOnlyOnce()
    {
        AddDefaultPolicy(p => p.RaiseExceptionOnLongBreak = true);
        AddPunches("I@08:00,BO@12:00,BI@13:30");
        Clock.Local = Monday.At(23, 0);
        await ProcessAsync(Monday);

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var missing = db.AttendanceExceptions.Single(e => e.ExceptionType == AttendanceExceptionType.MissingPunch);
            var refused = await AcceptAsync(missing.Id, _managerId, asHr: false, "Fine.");
            Assert.Contains("missing punch is corrected", refused.Errors["id"]);
        }

        Assert.True((await AcceptAsync(ExceptionId(), _managerId, asHr: false, "Fine.")).Succeeded);
        Assert.Contains("closed", (await AcceptAsync(ExceptionId(), _managerId, asHr: false, "Again.")).Errors["id"]);
        Assert.True((await AcceptAsync(99999, _managerId, asHr: false, "Nothing.")).NotFound);
    }

    [Fact]
    public async Task TheExceptionsList_OffersAccept_ToTheManagerAndHr_NotToTheEmployeeOrAnotherManager()
    {
        AddDefaultPolicy(p => p.RaiseExceptionOnLongBreak = true);
        AddPunches("I@08:00,BO@12:00,BI@13:30,O@17:00");
        await ProcessAsync(Monday);

        var asManager = await ListAsync(new ExceptionViewer(_managerId, false, true, false));
        var asEmployee = await ListAsync(new ExceptionViewer(EmployeeId, true, false, false));
        var asHr = await ListAsync(new ExceptionViewer(900, false, false, true));
        var asOtherManager = await ListAsync(new ExceptionViewer(901, false, true, false));

        Assert.True(Assert.Single(asManager).CanAccept);
        Assert.False(Assert.Single(asEmployee).CanAccept);
        Assert.True(Assert.Single(asHr).CanAccept);
        Assert.Empty(asOtherManager);
    }

    [Fact]
    public async Task TheDayShowsItsBreakFindings_ToThePeopleWhoReadIt()
    {
        AddPunches("I@08:00,BO@13:00,BI@14:30,O@17:00");
        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var service = new AttendanceRecordService(db, CreateEngine(db), CreateAudit(db));
        var day = await service.GetDayAsync(EmployeeId, Monday, new ExceptionViewer(900, false, false, true), CancellationToken.None);

        Assert.Equal(["LongBreak", "LateBreak"], Assert.Single(day!.Versions).BreakFindings);
    }

    private int ExceptionId()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return db.AttendanceExceptions.Where(e => e.ExceptionType != AttendanceExceptionType.MissingPunch).OrderByDescending(e => e.Id).First().Id;
    }

    private async Task<robeem.Application.Common.SaveResult> AcceptAsync(int exceptionId, int by, bool asHr, string reason)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await new AttendanceExceptionService(db, CreateEngine(db), CreateAudit(db), Clock).AcceptAsync(exceptionId, by, asHr, reason, CancellationToken.None);
    }

    private async Task<IReadOnlyList<ExceptionListItem>> ListAsync(ExceptionViewer viewer)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await new AttendanceExceptionQuery(db).ListAsync(new ExceptionFilter(ExceptionStatusFilter.Active, null, null, null, 100), viewer, CancellationToken.None);
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
