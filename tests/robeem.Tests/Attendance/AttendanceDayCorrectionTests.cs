using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// Correcting a whole day: the employee sees every punch and sends, with one reason, the edits that put the day right (change a punch's type,
/// add a missing punch, ignore a punch that should not count); the approvers decide them as one request. The shift is 08:00-17:00 without a break.
/// </summary>
public sealed class AttendanceDayCorrectionTests : AttendanceEngineTestBase
{
    private readonly int _managerId;

    public AttendanceDayCorrectionTests()
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

        AssignSchedule(new ShiftSpec("08:00", "17:00"));
        Clock.Local = Monday.At(18, 0);
    }

    [Fact]
    public async Task TheDayShowsEveryPunch_InOrder_WithWhatIsWrongWithOneAndWhatWouldSettleIt()
    {
        AddPunches("I@08:00,O@13:00,O@13:05,O@17:00");
        await ProcessAsync(Monday);

        var day = (await GetDayAsync())!;

        Assert.Equal(["Check in", "Check out", "Check out", "Check out"], day.Punches.Select(p => Words(p.Type)));
        Assert.Equal([Monday.At(8, 0), Monday.At(13, 0), Monday.At(13, 5), Monday.At(17, 0)], day.Punches.Select(p => p.At));
        Assert.All(day.Punches, p => Assert.True(p.Editable));
        Assert.Equal(["08:00–17:00"], day.Shifts);
        Assert.True(day.CanCorrect);

        // The two check-outs that follow a check-out are out of order, and each says so.
        var stray = day.Punches.Where(p => p.Problem is not null).ToList();
        Assert.Equal([Monday.At(13, 5), Monday.At(17, 0)], stray.Select(p => p.At));
        Assert.Contains("A check-out follows a check-out", stray[0].Problem);
        Assert.Equal(2, day.Exceptions.Count);
    }

    [Fact]
    public async Task OnlyTheEmployeeTheirManagerAndHrCanSeeTheirDay_AndReadingItIsAudited()
    {
        AddPunches("I@08:00,O@13:00,O@13:05");
        await ProcessAsync(Monday);
        var stranger = AddPerson("S-1", "Alan", "Turing");

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            // A manager's role counts only for their own reports, and a role without the identity is nothing.
            Assert.Null(await Service(db).GetDayAsync(EmployeeId, Monday, Viewer(_managerId), CancellationToken.None));
            Assert.Null(await Service(db).GetDayAsync(EmployeeId, Monday, Viewer(stranger, manager: true), CancellationToken.None));
            Assert.Null(await Service(db).GetDayAsync(EmployeeId, Monday, Viewer(stranger, employee: true), CancellationToken.None));
            Assert.Null(await Service(db).GetDayAsync(9999, Monday, Viewer(9999), CancellationToken.None));
            Assert.Empty(db.AttendanceAuditLogs.Where(a => a.Action == AttendanceAuditActions.AttendanceDataRead));

            var byManager = await Service(db).GetDayAsync(EmployeeId, Monday, Viewer(_managerId, manager: true), CancellationToken.None);
            var byHr = await Service(db).GetDayAsync(EmployeeId, Monday, Viewer(stranger, hr: true), CancellationToken.None);
            Assert.True(byManager!.IsOnBehalf);
            Assert.True(byHr!.IsOnBehalf);
            Assert.Equal("Ada Lovelace", byManager.EmployeeName);
        }

        var own = (await GetDayAsync())!;
        Assert.False(own.IsOnBehalf);
        Assert.Null(own.OnBehalfOutcome);

        // Every read says who read: the employee's own, and the manager's and HR's on their behalf.
        using var check = Database.CreateContext(TestDatabase.TenantA);
        var reads = check.AttendanceAuditLogs.Where(a => a.Action == AttendanceAuditActions.AttendanceDataRead).OrderBy(a => a.Id).ToList();
        Assert.Equal([$"employee:{_managerId}", $"employee:{stranger}", $"employee:{EmployeeId}"], reads.Select(a => a.Actor));
        Assert.All(reads, a => Assert.Contains("for a correction", a.Details));
        Assert.Contains($"on their behalf by employee {_managerId}", reads[0].Details);
        Assert.DoesNotContain("on their behalf", reads[2].Details);
    }

    [Fact]
    public async Task AManagerCorrectsTheirReportsDay_AndItIsAppliedAtOnce_BecauseTheirStepIsTheOnlyOne()
    {
        AllowShortDays();
        AddPunches("I@08:00,O@13:00,O@13:05,O@17:00");
        await ProcessAsync(Monday);
        var stray = (await GetDayAsync())!.Punches.Single(p => p.At == Monday.At(13, 5)).TransactionId!.Value;
        var manager = Viewer(_managerId, manager: true);

        var view = await GetDayForAsync(manager);
        Assert.Equal(DayCorrectionOutcome.AppliedAtOnce, view.OnBehalfOutcome);

        var sent = await ProposeForAsync(
            manager,
            Request(
                "Ada told me she clocked out for lunch twice and came back at two",
                new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = stray },
                new DayCorrectionEdit { Action = DayCorrectionRequest.Add, EventType = "CheckIn", At = Monday.At(14, 0) }));

        Assert.True(sent.Succeeded, string.Join("; ", sent.Errors.Values));
        using var check = Database.CreateContext(TestDatabase.TenantA);

        // One request, made by the manager for Ada, with their decision recorded in the manager's step, already applied.
        var approval = Assert.Single(check.AttendanceApprovals.Where(a => a.Type == AttendanceApprovalType.Correction));
        Assert.Equal((AttendanceApprovalStatus.Applied, EmployeeId, _managerId), (approval.Status, approval.EmployeeId, approval.RequestedByEmployeeId));
        Assert.Equal((AttendanceCorrectionDecision.Approved, _managerId), (approval.ManagerDecision!.Value, approval.ManagerDecidedById!.Value));
        Assert.Null(approval.HrDecision);
        Assert.NotNull(approval.AppliedAt);
        Assert.All(check.AttendanceCorrections, c => Assert.Equal((_managerId, EmployeeId, false), (c.RequestedByEmployeeId, c.EmployeeId, c.IsPending)));

        // The day was calculated again: the exceptions are closed by the correction and the day is whole.
        Assert.All(check.AttendanceExceptions, e => Assert.Equal((AttendanceExceptionStatus.Closed, AttendanceExceptionResolutionMethod.Correction), (e.Status, e.ResolutionMethod)));
        var record = check.AttendanceRecords.Single(r => r.IsCurrent);
        Assert.Equal((TimeSpan.FromHours(8), AttendanceRecordStatus.Approved), (record.WorkingHours, record.Status));

        // Nothing waits in any queue, and the raw punch is still there.
        Assert.Empty(await Corrections(check).ListPendingAsync(CorrectionApprover.Manager, _managerId, CancellationToken.None));
        Assert.Equal(4, check.NormalizedAttendanceTransactions.Count());

        // The log says who did what, for whom.
        var log = check.AttendanceAuditLogs.ToList();
        Assert.Contains(log, a => a.Action == AttendanceAuditActions.CorrectionDecided && a.Actor == $"employee:{_managerId}" && a.Details!.Contains($"on behalf of employee {EmployeeId}") && a.Details.Contains("Applied"));
    }

    [Fact]
    public async Task ACorrectionForSomeoneElse_NeedsAReason_AndOnlyTheirManagerOrHrMaySendIt()
    {
        AllowShortDays();
        AddPunches("I@08:00,O@13:00,O@13:05,O@17:00");
        await ProcessAsync(Monday);
        var stray = (await GetDayAsync())!.Punches.Single(p => p.At == Monday.At(13, 5)).TransactionId!.Value;
        var stranger = AddPerson("S-1", "Alan", "Turing");
        var ignore = new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = stray };

        var noReason = await ProposeForAsync(Viewer(_managerId, manager: true), Request("  ", ignore));
        var notTheManager = await ProposeForAsync(Viewer(stranger, manager: true), Request("Because", ignore));
        var anEmployee = await ProposeForAsync(Viewer(stranger, employee: true), Request("Because", ignore));
        var noSuchEmployee = await ProposeForAsync(Viewer(stranger, hr: true), Request("Because", ignore), employeeId: 9999);

        Assert.Contains(nameof(DayCorrectionRequest.Justification), noReason.Errors.Keys);
        Assert.Contains(CorrectionErrorKeys.RequestedBy, notTheManager.Errors.Keys);
        Assert.Contains(CorrectionErrorKeys.RequestedBy, anEmployee.Errors.Keys);
        Assert.Contains(CorrectionErrorKeys.RequestedBy, noSuchEmployee.Errors.Keys);

        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(check.AttendanceApprovals.Where(a => a.Type == AttendanceApprovalType.Correction));
        Assert.Empty(check.AttendanceCorrections);
        Assert.All(check.AttendanceExceptions, e => Assert.Equal(AttendanceExceptionStatus.Open, e.Status));
    }

    [Fact]
    public async Task WhenTheWorkflowAlsoNeedsHr_TheManagersCorrectionWaitsForHrOnly_AndHrDecidesItAsAnyone()
    {
        UseWorkflow(CorrectionApprovalWorkflow.Both);
        AddPunches("I@08:00,O@13:00,O@13:05,O@17:00");
        await ProcessAsync(Monday);
        var stray = (await GetDayAsync())!.Punches.Single(p => p.At == Monday.At(13, 5)).TransactionId!.Value;
        var hr = AddPerson("H-1", "Alan", "Turing");
        var manager = Viewer(_managerId, manager: true);

        Assert.Equal(DayCorrectionOutcome.AwaitsHr, (await GetDayForAsync(manager)).OnBehalfOutcome);
        var sent = await ProposeForAsync(manager, Request("Ada asked me to", FixTheDay(stray)));
        Assert.True(sent.Succeeded, string.Join("; ", sent.Errors.Values));

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var approval = Assert.Single(CorrectionApprovals(db));
            Assert.Equal((AttendanceApprovalStatus.Pending, AttendanceCorrectionDecision.Approved, null), (approval.Status, approval.ManagerDecision, approval.HrDecision));

            // Not in the manager's queue (they have already decided, and could not decide their own request anyway), but in HR's.
            Assert.Empty(await Corrections(db).ListPendingAsync(CorrectionApprover.Manager, _managerId, CancellationToken.None));
            var queued = Assert.Single(await Corrections(db).ListPendingAsync(CorrectionApprover.HR, hr, CancellationToken.None));
            Assert.All(db.AttendanceExceptions, e => Assert.Equal(AttendanceExceptionStatus.PendingCorrection, e.Status));

            // The proposer can't approve it themselves; HR can.
            var self = await Corrections(db).DecideCorrectionAsync(queued.Id, _managerId, approved: true, isHrDecision: true, CancellationToken.None);
            Assert.Contains("proposed", self.Errors.Values.Single());
            var decided = await Corrections(db).DecideCorrectionAsync(queued.Id, hr, approved: true, isHrDecision: true, CancellationToken.None);
            Assert.Equal(AttendanceApprovalStatus.Applied, decided.Status);
        }
    }

    [Fact]
    public async Task HrCorrectingForAManagedEmployee_UnderBoth_WaitsOnlyForTheManager_WhoThenCompletesIt()
    {
        UseWorkflow(CorrectionApprovalWorkflow.Both);
        AddPunches("I@08:00,O@13:00,O@13:05,O@17:00");
        await ProcessAsync(Monday);
        var stray = (await GetDayAsync())!.Punches.Single(p => p.At == Monday.At(13, 5)).TransactionId!.Value;
        var hr = AddPerson("H-1", "Alan", "Turing");

        Assert.Equal(DayCorrectionOutcome.AwaitsManager, (await GetDayForAsync(Viewer(hr, hr: true))).OnBehalfOutcome);
        Assert.True((await ProposeForAsync(Viewer(hr, hr: true), Request("Ada asked HR", FixTheDay(stray)))).Succeeded);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var approval = Assert.Single(CorrectionApprovals(db));
        Assert.Equal((AttendanceApprovalStatus.Pending, null, AttendanceCorrectionDecision.Approved), (approval.Status, approval.ManagerDecision, approval.HrDecision));
        Assert.Empty(await Corrections(db).ListPendingAsync(CorrectionApprover.HR, hr, CancellationToken.None));
        var queued = Assert.Single(await Corrections(db).ListPendingAsync(CorrectionApprover.Manager, _managerId, CancellationToken.None));

        // HR already took their step, so the manager's decision completes it.
        var decided = await Corrections(db).DecideCorrectionAsync(queued.Id, _managerId, approved: true, isHrDecision: false, CancellationToken.None);
        Assert.Equal(AttendanceApprovalStatus.Applied, decided.Status);
    }

    [Fact]
    public async Task HrCorrectingForAnEmployeeWithNoManager_StandsInForTheManagerToo_SoItIsAppliedAtOnce()
    {
        UseWorkflow(CorrectionApprovalWorkflow.Both);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.Profiles.Single(p => p.Id == EmployeeId).ManagerId = null;
            db.SaveChanges();
        }

        AddPunches("I@08:00,O@13:00,O@13:05,O@17:00");
        await ProcessAsync(Monday);
        var stray = (await GetDayAsync())!.Punches.Single(p => p.At == Monday.At(13, 5)).TransactionId!.Value;
        var hr = AddPerson("H-1", "Alan", "Turing");

        Assert.True((await ProposeForAsync(Viewer(hr, hr: true), Request("No manager to ask", FixTheDay(stray)))).Succeeded);

        using var check = Database.CreateContext(TestDatabase.TenantA);
        var approval = Assert.Single(CorrectionApprovals(check));
        Assert.Equal((AttendanceApprovalStatus.Applied, hr, hr), (approval.Status, approval.ManagerDecidedById, approval.HrDecidedById));
    }

    [Fact]
    public async Task HrCorrectingUnderManagerOnly_ForAManagedEmployee_LeavesTheDecisionToTheManager()
    {
        AllowShortDays();
        AddPunches("I@08:00,O@13:00,O@13:05,O@17:00");
        await ProcessAsync(Monday);
        var stray = (await GetDayAsync())!.Punches.Single(p => p.At == Monday.At(13, 5)).TransactionId!.Value;
        var hr = AddPerson("H-1", "Alan", "Turing");

        Assert.True((await ProposeForAsync(Viewer(hr, hr: true), Request("Ada asked HR", FixTheDay(stray)))).Succeeded);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var approval = Assert.Single(CorrectionApprovals(db));
        Assert.Equal((AttendanceApprovalStatus.Pending, null, null), (approval.Status, approval.ManagerDecision, approval.HrDecision));
        Assert.Single(await Corrections(db).ListPendingAsync(CorrectionApprover.Manager, _managerId, CancellationToken.None));
    }

    [Fact]
    public async Task TheEmployeesOwnCorrection_IsStillSentForApproval_EvenWhenTheyAreAlsoAManager()
    {
        AllowShortDays();
        AddPunches("I@08:00,O@13:00,O@13:05,O@17:00");
        await ProcessAsync(Monday);
        var stray = (await GetDayAsync())!.Punches.Single(p => p.At == Monday.At(13, 5)).TransactionId!.Value;

        // Their roles do not matter for their own day: nobody approves their own correction.
        var sent = await ProposeForAsync(Viewer(EmployeeId, employee: true, manager: true, hr: true), Request("Mine", FixTheDay(stray)), employeeId: EmployeeId);

        Assert.True(sent.Succeeded, string.Join("; ", sent.Errors.Values));
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var approval = Assert.Single(CorrectionApprovals(db));
        Assert.Equal((AttendanceApprovalStatus.Pending, null, null), (approval.Status, approval.ManagerDecision, approval.HrDecision));
    }

    [Fact]
    public async Task IgnoringAStrayPunch_AndAddingAForgottenOne_AreDecidedTogether_AndTheDayIsRightAfterwards()
    {
        AllowShortDays();
        AddPunches("I@08:00,O@13:00,O@13:05,O@17:00");
        await ProcessAsync(Monday);
        var stray = (await GetDayAsync())!.Punches.Single(p => p.At == Monday.At(13, 5)).TransactionId!.Value;

        var sent = await ProposeAsync(
            Request(
                "I clocked out twice for lunch and forgot to clock back in",
                new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = stray },
                new DayCorrectionEdit { Action = DayCorrectionRequest.Add, EventType = "CheckIn", At = Monday.At(14, 0) }));

        Assert.True(sent.Succeeded);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var rows = db.AttendanceCorrections.OrderBy(c => c.Id).ToList();
            Assert.Equal([AttendanceCorrectionKind.IgnorePunch, AttendanceCorrectionKind.SupplyPunch], rows.Select(c => c.Kind));
            Assert.Single(rows.Select(c => c.AttendanceApprovalId).Distinct());
            Assert.All(rows, c => Assert.Null(c.AttendanceExceptionId));
            var approval = Assert.Single(db.AttendanceApprovals.Where(a => a.Type == AttendanceApprovalType.Correction));
            Assert.Equal((AttendanceApprovalType.Correction, AttendanceApprovalStatus.Pending), (approval.Type, approval.Status));

            // The day's exceptions wait for the decision together.
            Assert.All(db.AttendanceExceptions, e => Assert.Equal(AttendanceExceptionStatus.PendingCorrection, e.Status));
        }

        // The approver sees one request with both edits, and decides it once.
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var item = Assert.Single(await Corrections(db).ListPendingAsync(CorrectionApprover.Manager, _managerId, CancellationToken.None));
            Assert.Equal(["IgnorePunch", "SupplyPunch"], item.Edits.Select(e => e.Kind));
            Assert.Null(item.ExceptionId);

            var decided = await Corrections(db).DecideCorrectionAsync(item.Id, _managerId, approved: true, isHrDecision: false, CancellationToken.None);
            Assert.Equal(AttendanceApprovalStatus.Applied, decided.Status);
        }

        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceApprovalStatus.Applied, Assert.Single(check.AttendanceApprovals.Where(a => a.Type == AttendanceApprovalType.Correction)).Status);
        Assert.All(check.AttendanceExceptions.Where(e => e.ExceptionType == AttendanceExceptionType.InvalidSequence), e => Assert.Equal(AttendanceExceptionStatus.Closed, e.Status));

        // In 08:00, out 13:00, in 14:00 (added), out 17:00: five hours and three hours; the ignored punch counts for nothing.
        var record = check.AttendanceRecords.Single(r => r.IsCurrent);
        Assert.Equal((TimeSpan.FromHours(8), AttendanceRecordStatus.Approved), (record.WorkingHours, record.Status));
        Assert.Empty(check.AttendanceExceptions.Where(e => e.Status != AttendanceExceptionStatus.Closed));

        // The punch is still there: it is only not counted.
        Assert.Equal(4, check.NormalizedAttendanceTransactions.Count());
    }

    [Fact]
    public async Task AfterTheDecision_TheDayShowsWhatWasIgnoredAndAdded_AndCanNotBeEditedAgainOnThosePunches()
    {
        AllowShortDays();
        AddPunches("I@08:00,O@13:00,O@13:05,O@17:00");
        await ProcessAsync(Monday);
        var stray = (await GetDayAsync())!.Punches.Single(p => p.At == Monday.At(13, 5)).TransactionId!.Value;
        await ProposeAsync(Request("Because", new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = stray }, new DayCorrectionEdit { Action = DayCorrectionRequest.Add, EventType = "CheckIn", At = Monday.At(14, 0) }));
        await DecideFirstAsync(approved: true);

        var day = (await GetDayAsync())!;

        Assert.Equal(("Ignored", false), (day.Punches.Single(p => p.At == Monday.At(13, 5)).State, day.Punches.Single(p => p.At == Monday.At(13, 5)).Editable));
        var added = day.Punches.Single(p => p.State == "Added");
        Assert.Equal(("CheckIn", Monday.At(14, 0), "Corrected", (int?)null), (added.Type, added.At, added.Source, added.TransactionId));
        Assert.False(day.CanCorrect);
        Assert.Equal("This day has no exception to correct.", day.CannotCorrectReason);
    }

    [Fact]
    public async Task ADayBeforeTheStartDate_CanNotBeCorrected()
    {
        AddPunches("I@08:00,O@13:00,O@13:05,O@17:00");
        await ProcessAsync(Monday);
        var stray = (await GetDayAsync())!.Punches.Single(p => p.At == Monday.At(13, 5)).TransactionId!.Value;
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceSettings.Add(new AttendanceSettings { StartDate = Monday.AddDays(1) });
            db.SaveChanges();
        }

        var result = await ProposeAsync(Request("Because", new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = stray }));

        Assert.False(result.Succeeded);
        Assert.Contains("nothing can be entered for an earlier day", result.Errors["Date"]);
    }

    [Fact]
    public async Task ChangingATypeAlongsideAnIgnore_IsOneRequest_AndTheChangedPunchCountsAsItsNewType()
    {
        AddPunches("I@08:00,O@12:00,O@12:30,O@17:00");
        await ProcessAsync(Monday);
        var punches = (await GetDayAsync())!.Punches;
        var secondOut = punches.Single(p => p.At == Monday.At(12, 30)).TransactionId!.Value;

        // The 12:30 tap was really the check-in after lunch; the 17:00 one stays a check-out.
        var sent = await ProposeAsync(Request("Back from lunch", new DayCorrectionEdit { Action = DayCorrectionRequest.Change, TransactionId = secondOut, EventType = "CheckIn" }));
        await DecideFirstAsync(approved: true);

        Assert.True(sent.Succeeded);
        using var check = Database.CreateContext(TestDatabase.TenantA);

        // In 08:00, out 12:00 (4 hours), in 12:30 (the changed punch), out 17:00 (4.5 hours).
        Assert.Equal(TimeSpan.FromHours(8.5), check.AttendanceRecords.Single(r => r.IsCurrent).WorkingHours);
        Assert.Equal(secondOut, check.AttendanceCorrections.Single().NormalizedTransactionId);
    }

    [Fact]
    public async Task ARejectedRequest_ReopensTheExceptions_AndAllowsANewOne()
    {
        AddPunches("I@08:00,O@13:00,O@13:05");
        await ProcessAsync(Monday);
        var stray = (await GetDayAsync())!.Punches.Single(p => p.At == Monday.At(13, 5)).TransactionId!.Value;
        await ProposeAsync(Request("Because", new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = stray }));

        await DecideFirstAsync(approved: false);

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Equal(AttendanceApprovalStatus.Rejected, Assert.Single(db.AttendanceApprovals.Where(a => a.Type == AttendanceApprovalType.Correction)).Status);
            Assert.All(db.AttendanceExceptions, e => Assert.Equal(AttendanceExceptionStatus.Open, e.Status));
        }

        Assert.True((await ProposeAsync(Request("Again", new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = stray }))).Succeeded);
    }

    [Fact]
    public async Task AWholeRequestIsRefusedWhenOneEditIs_AndNothingIsStored()
    {
        AddPunches("I@08:00,O@13:00,O@13:05");
        await ProcessAsync(Monday);
        var punches = (await GetDayAsync())!.Punches;
        var stray = punches.Single(p => p.At == Monday.At(13, 5)).TransactionId!.Value;

        var result = await ProposeAsync(Request(
            "Because",
            new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = stray },
            new DayCorrectionEdit { Action = DayCorrectionRequest.Change, TransactionId = 99999, EventType = "CheckIn" },
            new DayCorrectionEdit { Action = DayCorrectionRequest.Add, EventType = "CheckIn", At = Monday.At(14, 0) }));

        Assert.Contains("Edit 2", result.Errors["Edits"]);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceCorrections);
        Assert.All(db.AttendanceExceptions, e => Assert.Equal(AttendanceExceptionStatus.Open, e.Status));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task AReasonIsOptional(string reason)
    {
        AddPunches("I@08:00,O@13:00,O@13:05");
        await ProcessAsync(Monday);
        var stray = (await GetDayAsync())!.Punches.Single(p => p.At == Monday.At(13, 5)).TransactionId!.Value;

        var result = await ProposeAsync(Request(reason, new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = stray }));

        Assert.True(result.Succeeded);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(string.Empty, db.AttendanceCorrections.Single().Justification);
    }

    [Fact]
    public async Task TheDayMustHaveACheckInAndACheckOut_AfterTheEdits_OrItIsNotSent()
    {
        AddPunches("I@08:00,O@13:00,O@13:05");
        await ProcessAsync(Monday);
        var punches = (await GetDayAsync())!.Punches;
        var stray = punches.Single(p => p.At == Monday.At(13, 5)).TransactionId!.Value;
        var lunchOut = punches.Single(p => p.At == Monday.At(13, 0)).TransactionId!.Value;
        var checkIn = punches.Single(p => p.At == Monday.At(8, 0)).TransactionId!.Value;

        // Ignoring both check-outs leaves the day without one.
        var noCheckOut = await ProposeAsync(Request(
            "Because",
            new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = stray },
            new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = lunchOut }));
        Assert.Contains("a check-out", noCheckOut.Errors["Edits"]);
        Assert.DoesNotContain("a check-in", noCheckOut.Errors["Edits"]);

        // Ignoring the check-in too leaves neither.
        var neither = await ProposeAsync(Request(
            "Because",
            new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = stray },
            new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = checkIn }));
        Assert.Contains("a check-in", neither.Errors["Edits"]);

        // Adding the missing one makes it complete.
        Assert.True((await ProposeAsync(Request(
            "Because",
            new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = stray },
            new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = lunchOut },
            new DayCorrectionEdit { Action = DayCorrectionRequest.Add, EventType = "CheckOut", At = Monday.At(17, 0) }))).Succeeded);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(3, db.AttendanceCorrections.Count());
    }

    [Fact]
    public async Task APunchNeverStandsWithoutItsPartner_EvenOnAShiftWithoutABreak()
    {
        AddPunches("I@08:00,O@13:00,O@13:05");
        await ProcessAsync(Monday);
        var punches = (await GetDayAsync())!.Punches;
        var lunchOut = punches.Single(p => p.At == Monday.At(13, 0)).TransactionId!.Value;
        var stray = punches.Single(p => p.At == Monday.At(13, 5)).TransactionId!.Value;

        // A break-in with no break-out, on a shift that has no break.
        var breakInOnly = await ProposeAsync(Request(string.Empty, new DayCorrectionEdit { Action = DayCorrectionRequest.Change, TransactionId = lunchOut, EventType = "BreakIn" }));
        Assert.Contains("a break-out", breakInOnly.Errors["Edits"]);

        // A break-out with no break-in.
        var breakOutOnly = await ProposeAsync(Request(string.Empty, new DayCorrectionEdit { Action = DayCorrectionRequest.Change, TransactionId = lunchOut, EventType = "BreakOut" }));
        Assert.Contains("a break-in", breakOutOnly.Errors["Edits"]);

        // A check-in added with the check-out gone, and a check-out with the check-in gone.
        var checkInOnly = await ProposeAsync(Request(
            string.Empty,
            new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = stray },
            new DayCorrectionEdit { Action = DayCorrectionRequest.Change, TransactionId = lunchOut, EventType = "CheckIn" }));
        Assert.Contains("a check-out", checkInOnly.Errors["Edits"]);
        var extraCheckOut = await ProposeAsync(Request(string.Empty, new DayCorrectionEdit { Action = DayCorrectionRequest.Add, EventType = "CheckOut", At = Monday.At(17, 0) }));
        Assert.Contains("a check-in", extraCheckOut.Errors["Edits"]);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceCorrections);
    }

    [Fact]
    public async Task APunchAnEarlierCorrectionAdded_CanBeDeleted_ByANewCorrection_AndTheEngineStopsReadingIt()
    {
        AllowShortDays();
        AddPunches("I@08:00,O@13:00,O@13:05");
        await ProcessAsync(Monday);
        var addedId = AddAppliedSuppliedCheckIn(Monday.At(14, 0));
        var day = (await GetDayAsync())!;
        var stray = day.Punches.Single(p => p.At == Monday.At(13, 5)).TransactionId!.Value;

        // The added punch is shown, and says which correction added it.
        var added = day.Punches.Single(p => p.State == "Added");
        Assert.Equal((addedId, (int?)null), (added.CorrectionId, added.TransactionId));

        var sent = await ProposeAsync(Request(
            string.Empty,
            new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, CorrectionId = addedId },
            new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = stray }));
        Assert.True(sent.Succeeded, string.Join("; ", sent.Errors.Values));

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            // Additive: the earlier correction is not edited, a new row points at it.
            var cancel = db.AttendanceCorrections.Single(c => c.Kind == AttendanceCorrectionKind.CancelSuppliedPunch);
            Assert.Equal(addedId, cancel.CancelsCorrectionId);
            Assert.Equal(AttendanceApprovalStatus.Applied, db.AttendanceCorrections.Where(c => c.Id == addedId).Select(c => c.AttendanceApproval!.Status).Single());
        }

        await DecideFirstAsync(approved: true);

        // 08:00 in, 13:00 out: with the added check-in read, the day would still lack a check-out.
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(check.AttendanceExceptions.Where(e => e.Status != AttendanceExceptionStatus.Closed));
        Assert.Equal(TimeSpan.FromHours(5), check.AttendanceRecords.Single(r => r.IsCurrent).WorkingHours);

        var after = (await GetDayAsync())!;
        var deleted = after.Punches.Single(p => p.At == Monday.At(14, 0));
        Assert.Equal(("Ignored", (int?)null), (deleted.State, deleted.CorrectionId));
    }

    [Fact]
    public async Task DeletingAnAddedPunch_IsRefusedForAnUnknownCorrection_ForTheSameOneTwice_AndWhenItLeavesAPunchAlone()
    {
        AddPunches("I@08:00,O@13:00,O@13:05");
        await ProcessAsync(Monday);
        var addedId = AddAppliedSuppliedCheckIn(Monday.At(14, 0));

        var unknown = await ProposeAsync(Request(string.Empty, new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, CorrectionId = 99999 }));
        Assert.Contains("not a punch added", unknown.Errors["Edits"]);

        var twice = await ProposeAsync(Request(
            string.Empty,
            new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, CorrectionId = addedId },
            new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, CorrectionId = addedId }));
        Assert.Contains("already deleted", twice.Errors["Edits"]);

        // Without the added check-in, the stray check-out is still there with only one check-in: the pair rule applies here too.
        var alone = await ProposeAsync(Request(string.Empty, new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, CorrectionId = addedId }));
        Assert.Contains("a check-in", alone.Errors["Edits"]);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.DoesNotContain(db.AttendanceCorrections, c => c.Kind == AttendanceCorrectionKind.CancelSuppliedPunch);
    }

    [Fact]
    public async Task ThePunchesMustFollowTheOrder_CheckIn_BreakOut_BreakIn_CheckOut()
    {
        GiveTheShiftABreak();
        AddPunches("I@08:00,O@13:00,O@13:05");
        await ProcessAsync(Monday);
        var punches = (await GetDayAsync())!.Punches;
        var lunchOut = punches.Single(p => p.At == Monday.At(13, 0)).TransactionId!.Value;

        // All four types are there, but the break-in comes before the break-out.
        var reversed = await ProposeAsync(Request(
            string.Empty,
            new DayCorrectionEdit { Action = DayCorrectionRequest.Change, TransactionId = lunchOut, EventType = "BreakIn" },
            new DayCorrectionEdit { Action = DayCorrectionRequest.Add, EventType = "BreakOut", At = Monday.At(14, 0) }));
        Assert.Contains("out of order", reversed.Errors["Edits"]);
        Assert.Contains("break-in at 13:00 can't come after a check-in", reversed.Errors["Edits"]);
        Assert.Contains("The order is check-in, break-out, break-in, check-out.", reversed.Errors["Edits"]);

        // The same two edits with the break-out before the break-in are in order.
        var inOrder = await ProposeAsync(Request(
            string.Empty,
            new DayCorrectionEdit { Action = DayCorrectionRequest.Change, TransactionId = lunchOut, EventType = "BreakIn" },
            new DayCorrectionEdit { Action = DayCorrectionRequest.Add, EventType = "BreakOut", At = Monday.At(12, 0) }));
        Assert.True(inOrder.Succeeded, string.Join("; ", inOrder.Errors.Values));
    }

    [Fact]
    public async Task ADayThatPassesTheOrderCheck_IsReadByTheEngineAsBreakOutThenBreakIn_WithNoBreakException()
    {
        GiveTheShiftABreak();
        AddPunches("I@08:00,O@13:00,O@17:00");
        await ProcessAsync(Monday);
        var lunchIn = (await GetDayAsync())!.Punches.Single(p => p.At == Monday.At(13, 0)).TransactionId!.Value;

        // 08:00 in, 12:00 out for lunch (added), 13:00 back in (the 13:00 tap), 17:00 out.
        var sent = await ProposeAsync(Request(
            "Lunch break",
            new DayCorrectionEdit { Action = DayCorrectionRequest.Change, TransactionId = lunchIn, EventType = "BreakIn" },
            new DayCorrectionEdit { Action = DayCorrectionRequest.Add, EventType = "BreakOut", At = Monday.At(12, 0) }));
        Assert.True(sent.Succeeded, string.Join("; ", sent.Errors.Values));
        await DecideFirstAsync(approved: true);

        using var check = Database.CreateContext(TestDatabase.TenantA);
        var record = check.AttendanceRecords.Single(r => r.IsCurrent);
        Assert.Equal((TimeSpan.FromHours(8), TimeSpan.FromHours(1), AttendanceRecordStatus.Approved), (record.WorkingHours, record.BreakDuration, record.Status));
        Assert.Empty(check.AttendanceExceptions.Where(e => e.Status != AttendanceExceptionStatus.Closed));
    }

    [Fact]
    public async Task TheDayViewNamesTheRequiredPunches_BreakPunchesOnlyForAShiftWithABreak()
    {
        AddPunches("I@08:00,O@13:00,O@13:05");
        await ProcessAsync(Monday);

        Assert.Equal(["CheckIn", "CheckOut"], (await GetDayAsync())!.RequiredPunches);
    }

    [Fact]
    public async Task AShiftWithABreak_NeedsTheBreakPunchesToo_UnlessThePolicyAllowsWorkingThroughIt()
    {
        GiveTheShiftABreak();
        AddPunches("I@08:00,O@13:00,O@13:05");
        await ProcessAsync(Monday);
        var day = (await GetDayAsync())!;
        var stray = day.Punches.Single(p => p.At == Monday.At(13, 5)).TransactionId!.Value;

        Assert.Equal(["CheckIn", "BreakOut", "BreakIn", "CheckOut"], day.RequiredPunches);
        var refused = await ProposeAsync(Request(string.Empty, new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = stray }));
        Assert.Contains("a break-out, a break-in", refused.Errors["Edits"]);

        // Adding the break-out at 12:00 and turning the 13:00 tap into the break-in settles it.
        var lunchIn = day.Punches.Single(p => p.At == Monday.At(13, 0)).TransactionId!.Value;
        var sent = await ProposeAsync(Request(
            string.Empty,
            new DayCorrectionEdit { Action = DayCorrectionRequest.Change, TransactionId = lunchIn, EventType = "BreakIn" },
            new DayCorrectionEdit { Action = DayCorrectionRequest.Add, EventType = "BreakOut", At = Monday.At(12, 0) }));
        Assert.True(sent.Succeeded, string.Join("; ", sent.Errors.Values));
    }

    [Fact]
    public async Task EachKindOfBadEdit_IsRefusedWithItsReason()
    {
        AddPunches("I@08:00,O@13:00,O@13:05");
        await ProcessAsync(Monday);
        var punches = (await GetDayAsync())!.Punches;
        var stray = punches.Single(p => p.At == Monday.At(13, 5)).TransactionId!.Value;
        var lunchOut = punches.Single(p => p.At == Monday.At(13, 0)).TransactionId!.Value;

        async Task<string> Refusal(params DayCorrectionEdit[] edits) => (await ProposeAsync(Request("Because", edits))).Errors["Edits"];

        Assert.Contains("already a CheckOut", await Refusal(new DayCorrectionEdit { Action = DayCorrectionRequest.Change, TransactionId = stray, EventType = "CheckOut" }));
        Assert.Contains("not a type of punch", await Refusal(new DayCorrectionEdit { Action = DayCorrectionRequest.Change, TransactionId = stray, EventType = "Lunch" }));

        var ignoreAndChange = await Refusal(
            new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = stray },
            new DayCorrectionEdit { Action = DayCorrectionRequest.Change, TransactionId = stray, EventType = "CheckIn" });
        Assert.Contains("already changed or ignored", ignoreAndChange);
        Assert.Contains("say when", await Refusal(new DayCorrectionEdit { Action = DayCorrectionRequest.Add, EventType = "CheckIn" }));
        Assert.Contains("the day being corrected", await Refusal(new DayCorrectionEdit { Action = DayCorrectionRequest.Add, EventType = "CheckIn", At = Monday.AddDays(1).At(9, 0) }));

        var twice = await Refusal(
            new DayCorrectionEdit { Action = DayCorrectionRequest.Add, EventType = "CheckIn", At = Monday.At(14, 0) },
            new DayCorrectionEdit { Action = DayCorrectionRequest.Add, EventType = "CheckIn", At = Monday.At(14, 0) });
        Assert.Contains("added twice", twice);
        Assert.Contains("Use Change, Ignore or Add", await Refusal(new DayCorrectionEdit { Action = "Delete", TransactionId = lunchOut }));

        Clock.Local = Monday.At(14, 0);
        Assert.Contains("in the future", await Refusal(new DayCorrectionEdit { Action = DayCorrectionRequest.Add, EventType = "CheckIn", At = Monday.At(16, 0) }));
        Assert.Contains("Choose at least one", (await ProposeAsync(Request("Because"))).Errors["Edits"]);
    }

    [Fact]
    public async Task ADayWithoutAnOpenException_OrWithARequestWaiting_OrNotTheEmployees_CanNotBeCorrected()
    {
        AddPunches("I@08:00,O@17:00");
        await ProcessAsync(Monday);
        var clean = (await GetDayAsync())!;

        Assert.False(clean.CanCorrect);
        Assert.Equal("This day has no exception to correct.", clean.CannotCorrectReason);
        var punch = clean.Punches[0].TransactionId!.Value;
        Assert.Contains("no exception", (await ProposeAsync(Request("Because", new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = punch }))).Errors["Date"]);

        // Someone who is neither the employee, their manager nor HR can't correct it, whatever they send.
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var byOther = await Service(db).ProposeAsync(EmployeeId, Request("Because"), Viewer(_managerId), CancellationToken.None);
        Assert.Contains(CorrectionErrorKeys.RequestedBy, byOther.Errors.Keys);
    }

    [Fact]
    public async Task WhileARequestWaits_TheDayCanNotBeCorrectedAgain()
    {
        AddPunches("I@08:00,O@13:00,O@13:05");
        await ProcessAsync(Monday);
        var stray = (await GetDayAsync())!.Punches.Single(p => p.At == Monday.At(13, 5)).TransactionId!.Value;
        await ProposeAsync(Request("Because", new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = stray }));

        var day = (await GetDayAsync())!;
        var second = await ProposeAsync(Request("Again", new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = stray }));

        Assert.False(day.CanCorrect);
        Assert.Contains("already waiting", day.CannotCorrectReason);
        Assert.Contains("already waiting", second.Errors["Date"]);
    }

    [Fact]
    public async Task ADuplicateTheDeviceAlreadyLeftOut_IsShownButNotEditable()
    {
        AddPunches("I@08:00,O@13:00,O@17:00");
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            // What normalization stores for a repeat of a punch: another row, marked as a duplicate of the first.
            var first = db.NormalizedAttendanceTransactions.Single(n => n.EventTimestampLocal == Monday.At(13, 0));
            db.NormalizedAttendanceTransactions.Add(new NormalizedAttendanceTransaction
            {
                EmployeeId = EmployeeId,
                DeviceId = first.DeviceId,
                IntegrationId = first.IntegrationId,
                RawTransactionId = first.RawTransactionId,
                AttendanceDate = first.AttendanceDate,
                EventTimestampUtc = first.EventTimestampUtc.AddSeconds(30),
                EventTimestampLocal = first.EventTimestampLocal.AddSeconds(30),
                TimeZoneId = first.TimeZoneId,
                NormalizedEventType = first.NormalizedEventType,
                ConfidenceStatus = first.ConfidenceStatus,
                DuplicateStatus = DuplicateStatus.DuplicateConfirmed,
                DuplicateOfTransactionId = first.Id,
                DuplicateBucketTimestampUtc = first.DuplicateBucketTimestampUtc,
            });
            db.SaveChanges();
        }

        await ProcessAsync(Monday);
        var day = (await GetDayAsync())!;

        var duplicate = day.Punches.Single(p => p.State == "Duplicate");
        Assert.False(duplicate.Editable);
        Assert.Equal(4, day.Punches.Count);
    }

    private static string Words(string type) => type switch
    {
        "CheckIn" => "Check in",
        "CheckOut" => "Check out",
        _ => type,
    };

    private static DayCorrectionRequest Request(string justification, params DayCorrectionEdit[] edits) =>
        new() { Date = Monday, Justification = justification, Edits = edits };

    private static ExceptionViewer Viewer(int employeeId, bool employee = false, bool manager = false, bool hr = false) => new(employeeId, employee, manager, hr);

    /// <summary>The edits that make 08:00 in, 13:00 out, 13:05 out, 17:00 out whole: the stray check-out goes, and the check-in after lunch is added.</summary>
    private static DayCorrectionEdit[] FixTheDay(int stray) =>
    [
        new() { Action = DayCorrectionRequest.Ignore, TransactionId = stray },
        new() { Action = DayCorrectionRequest.Add, EventType = "CheckIn", At = Monday.At(14, 0) },
    ];

    /// <summary>The correction requests only: the engine also queues a review approval for a record it leaves for review.</summary>
    private static IQueryable<AttendanceApproval> CorrectionApprovals(RobeemDbContext db) => db.AttendanceApprovals.Where(a => a.Type == AttendanceApprovalType.Correction);

    /// <summary>Stores a check-in that an earlier request added and an approver applied; the exceptions of the day stay as they were. Returns the correction's id.</summary>
    private int AddAppliedSuppliedCheckIn(DateTime at)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var correction = new AttendanceCorrection
        {
            AttendanceApproval = new AttendanceApproval
            {
                Type = AttendanceApprovalType.Correction,
                EmployeeId = EmployeeId,
                AttendanceDate = at.Date,
                RequestedByEmployeeId = EmployeeId,
                RequestedAt = at,
                Status = AttendanceApprovalStatus.Applied,
                AppliedAt = at,
            },
            EmployeeId = EmployeeId,
            AttendanceDate = at.Date,
            Kind = AttendanceCorrectionKind.SupplyPunch,
            CorrectedEventType = NormalizedEventType.CheckIn,
            ProposedValue = at,
            TimeZoneId = db.NormalizedAttendanceTransactions.First().TimeZoneId,
            Justification = "Forgot",
            RequestedByEmployeeId = EmployeeId,
            IsPending = false,
        };
        db.AttendanceCorrections.Add(correction);
        db.SaveChanges();
        return correction.Id;
    }

    /// <summary>The shift of the constructor gets a break, 12:00-13:00.</summary>
    private void GiveTheShiftABreak()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var shift = db.TimeScheduleShifts.Single();
        (shift.BreakTime, shift.BreakStartTime, shift.BreakEndTime) = (true, "12:00", "13:00");
        db.SaveChanges();
    }

    private int AddPerson(string number, string first, string last)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var person = new Profile { EmployeeNumber = number, FirstName = first, LastName = last, Title = $"{first} {last}" };
        db.Add(person);
        db.SaveChanges();
        return person.Id;
    }

    /// <summary>Gives the tenant's default policy this approval workflow, and forgives short days so that only the correction is under test.</summary>
    private void UseWorkflow(CorrectionApprovalWorkflow workflow) => AddDefaultPolicy(p => (p.LateGraceMinutes, p.CorrectionApprovalWorkflow) = (600, workflow));

    private async Task<DayCorrectionView?> GetDayAsync()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await Service(db).GetDayAsync(EmployeeId, Monday, Viewer(EmployeeId, employee: true), CancellationToken.None);
    }

    /// <summary>Opens Ada's day as another user, on their behalf.</summary>
    private async Task<DayCorrectionView> GetDayForAsync(ExceptionViewer viewer)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return (await Service(db).GetDayAsync(EmployeeId, Monday, viewer, CancellationToken.None))!;
    }

    private async Task<robeem.Application.Common.SaveResult> ProposeAsync(DayCorrectionRequest request)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await Service(db).ProposeAsync(EmployeeId, request, Viewer(EmployeeId, employee: true), CancellationToken.None);
    }

    /// <summary>Sends a correction of Ada's day as another user, on their behalf (or of the employee named).</summary>
    private async Task<robeem.Application.Common.SaveResult> ProposeForAsync(ExceptionViewer viewer, DayCorrectionRequest request, int? employeeId = null)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await Service(db).ProposeAsync(employeeId ?? EmployeeId, request, viewer, CancellationToken.None);
    }

    private async Task DecideFirstAsync(bool approved)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var first = db.AttendanceCorrections.OrderBy(c => c.Id).First(c => c.AttendanceApproval!.Status == AttendanceApprovalStatus.Pending);
        await Corrections(db).DecideCorrectionAsync(first.Id, _managerId, approved, isHrDecision: false, CancellationToken.None);
    }

    private AttendanceDayCorrectionService Service(RobeemDbContext db) =>
        new(db, new ScheduleResolutionService(db), new AttendanceExceptionQuery(db), new AttendancePolicyResolver(db), new RuleSetProvider(db), CreateAudit(db), CreateEngine(db), Clock);

    private AttendanceCorrectionService Corrections(RobeemDbContext db) =>
        new(db, new AttendancePolicyResolver(db), new AttendanceApprovalService(db, CreateEngine(db), CreateAudit(db), Clock), CreateAudit(db), Clock);

    /// <summary>
    /// For a test about corrections, not about hours: the days it corrects end up shorter than the 08:00-17:00 shift, which would raise an exception
    /// of its own, so the policy forgives the time (the allowance that starts the rule for a day worked short).
    /// </summary>
    private void AllowShortDays() => AddDefaultPolicy(p => p.LateGraceMinutes = 600);

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
