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
    public async Task OnlyTheEmployeeCanSeeTheirDay_AndReadingItIsAudited()
    {
        AddPunches("I@08:00,O@13:00,O@13:05");
        await ProcessAsync(Monday);

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Null(await Service(db).GetDayAsync(EmployeeId, Monday, _managerId, CancellationToken.None));
            Assert.Null(await Service(db).GetDayAsync(9999, Monday, 9999, CancellationToken.None));
        }

        await GetDayAsync();

        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Contains(check.AttendanceAuditLogs.ToList(), a => a.Action == AttendanceAuditActions.AttendanceDataRead && a.Details!.Contains("for a correction"));
    }

    [Fact]
    public async Task IgnoringAStrayPunch_AndAddingAForgottenOne_AreDecidedTogether_AndTheDayIsRightAfterwards()
    {
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
            Assert.Single(rows.Select(c => c.RequestId).Distinct());
            Assert.All(rows, c => Assert.Equal((AttendanceCorrectionStatus.Pending, null), (c.Status, c.AttendanceExceptionId)));

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
            Assert.Equal(AttendanceCorrectionStatus.Applied, decided.Status);
        }

        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.All(check.AttendanceCorrections, c => Assert.Equal(AttendanceCorrectionStatus.Applied, c.Status));
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
            Assert.All(db.AttendanceCorrections, c => Assert.Equal(AttendanceCorrectionStatus.Rejected, c.Status));
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
            Assert.Equal(AttendanceCorrectionStatus.Applied, db.AttendanceCorrections.Single(c => c.Id == addedId).Status);
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
    public async Task ThePunchesMustFollowTheOrder_CheckIn_BreakIn_BreakOut_CheckOut()
    {
        GiveTheShiftABreak();
        AddPunches("I@08:00,O@13:00,O@13:05");
        await ProcessAsync(Monday);
        var punches = (await GetDayAsync())!.Punches;
        var lunchOut = punches.Single(p => p.At == Monday.At(13, 0)).TransactionId!.Value;

        // All four types are there, but the break-in comes after the check-out.
        var late = await ProposeAsync(Request(
            string.Empty,
            new DayCorrectionEdit { Action = DayCorrectionRequest.Change, TransactionId = lunchOut, EventType = "BreakOut" },
            new DayCorrectionEdit { Action = DayCorrectionRequest.Add, EventType = "BreakIn", At = Monday.At(14, 0) }));
        Assert.Contains("out of order", late.Errors["Edits"]);
        Assert.Contains("break-out at 13:00 can't come after a check-in", late.Errors["Edits"]);

        // The same two edits with the break-in before the break-out are in order.
        var inOrder = await ProposeAsync(Request(
            string.Empty,
            new DayCorrectionEdit { Action = DayCorrectionRequest.Change, TransactionId = lunchOut, EventType = "BreakOut" },
            new DayCorrectionEdit { Action = DayCorrectionRequest.Add, EventType = "BreakIn", At = Monday.At(12, 0) }));
        Assert.True(inOrder.Succeeded, string.Join("; ", inOrder.Errors.Values));
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

        Assert.Equal(["CheckIn", "BreakIn", "BreakOut", "CheckOut"], day.RequiredPunches);
        var refused = await ProposeAsync(Request(string.Empty, new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = stray }));
        Assert.Contains("a break-in, a break-out", refused.Errors["Edits"]);

        // Adding the break-in at 12:00 and turning the 13:00 tap into the break-out settles it.
        var lunchOut = day.Punches.Single(p => p.At == Monday.At(13, 0)).TransactionId!.Value;
        var sent = await ProposeAsync(Request(
            string.Empty,
            new DayCorrectionEdit { Action = DayCorrectionRequest.Change, TransactionId = lunchOut, EventType = "BreakOut" },
            new DayCorrectionEdit { Action = DayCorrectionRequest.Add, EventType = "BreakIn", At = Monday.At(12, 0) }));
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

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var byManager = await Service(db).ProposeAsync(EmployeeId, Request("Because"), _managerId, CancellationToken.None);
        Assert.Contains(CorrectionErrorKeys.RequestedBy, byManager.Errors.Keys);
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

    /// <summary>Stores a check-in that an earlier request added and an approver applied; the exceptions of the day stay as they were. Returns the correction's id.</summary>
    private int AddAppliedSuppliedCheckIn(DateTime at)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var correction = new AttendanceCorrection
        {
            EmployeeId = EmployeeId,
            AttendanceDate = at.Date,
            Kind = AttendanceCorrectionKind.SupplyPunch,
            CorrectedEventType = NormalizedEventType.CheckIn,
            ProposedValue = at,
            TimeZoneId = db.NormalizedAttendanceTransactions.First().TimeZoneId,
            Justification = "Forgot",
            RequestedByEmployeeId = EmployeeId,
            Status = AttendanceCorrectionStatus.Applied,
            AppliedAt = at,
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

    private async Task<DayCorrectionView?> GetDayAsync()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await Service(db).GetDayAsync(EmployeeId, Monday, EmployeeId, CancellationToken.None);
    }

    private async Task<robeem.Application.Common.SaveResult> ProposeAsync(DayCorrectionRequest request)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await Service(db).ProposeAsync(EmployeeId, request, EmployeeId, CancellationToken.None);
    }

    private async Task DecideFirstAsync(bool approved)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var first = db.AttendanceCorrections.OrderBy(c => c.Id).First(c => c.Status == AttendanceCorrectionStatus.Pending);
        await Corrections(db).DecideCorrectionAsync(first.Id, _managerId, approved, isHrDecision: false, CancellationToken.None);
    }

    private AttendanceDayCorrectionService Service(RobeemDbContext db) =>
        new(db, new ScheduleResolutionService(db), new AttendanceExceptionQuery(db), new AttendancePolicyResolver(db), CreateAudit(db), Clock);

    private AttendanceCorrectionService Corrections(RobeemDbContext db) =>
        new(db, new AttendancePolicyResolver(db), CreateEngine(db), CreateAudit(db), Clock);

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
