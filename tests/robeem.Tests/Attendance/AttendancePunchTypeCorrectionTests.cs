using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// Punches in the wrong order: the employee proposes the type the out-of-order punch should have (a check-out that follows a check-out
/// was really a check-in), only types that would settle the exception are offered, and an approved change makes the engine read the same
/// punch with its new type while the normalized punch stays as it was.
/// The shift is 08:00-17:00 without a break.
/// </summary>
public sealed class AttendancePunchTypeCorrectionTests : AttendanceEngineTestBase
{
    private readonly int _managerId;

    public AttendancePunchTypeCorrectionTests()
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
    public async Task ACheckOutThatFollowsACheckOut_OffersACheckIn_AndNothingThatWouldStillBeOutOfOrder()
    {
        AddPunches("I@08:00,O@12:00,O@13:00");
        await ProcessAsync(Monday);

        var exception = InvalidSequence();

        Assert.Equal("CheckIn", exception.SuggestedEventTypes);
        Assert.Equal((NormalizedEventType.CheckOut, Monday.At(13, 0)), (exception.RelatedEventType, exception.RelatedEventAt));
        Assert.Contains("A check-out follows a check-out", exception.Description);
    }

    [Fact]
    public async Task ABreakOutAfterTheCheckOut_CanBeACheckInOrOnlyThat_WhateverTheDeviceCalledIt()
    {
        AddPunches("I@08:00,O@12:00,BO@13:00");
        await ProcessAsync(Monday);

        var exception = InvalidSequence();

        // After the check-out the employee is off: a break-out or break-in there is out of order, a check-in is not.
        Assert.Equal("CheckIn", exception.SuggestedEventTypes);
    }

    [Fact]
    public async Task WhenNoOtherTypeSettlesIt_NothingIsOffered_AndTheEmployeeIsNotAskedToGuess()
    {
        // On a shift without a break, the second check-in can't be a break-out or break-in, and as a check-out the real check-out becomes the stray one.
        AddPunches("I@08:00,I@12:00,O@17:00");
        await ProcessAsync(Monday);

        var exception = InvalidSequence();

        Assert.Equal(string.Empty, exception.SuggestedEventTypes);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var listed = Assert.Single(await new AttendanceExceptionQuery(db).ListAsync(new ExceptionFilter(ExceptionStatusFilter.Active, null, null, null, 100), new ExceptionViewer(EmployeeId, true, false, false), CancellationToken.None));
        Assert.False(listed.CanPropose);
        Assert.Contains("No other type", (await ProposeAsync(exception.Id, "CheckOut")).Errors[nameof(ProposeCorrectionRequest.NewEventType)]);
    }

    [Fact]
    public async Task TheEmployeeProposesTheChange_TheManagerApproves_AndTheDayIsCalculatedWithThePunchAsACheckIn()
    {
        AddPunches("I@08:00,O@12:00,O@13:00");
        await ProcessAsync(Monday);
        var exceptionId = InvalidSequence().Id;

        var proposed = await ProposeAsync(exceptionId, "CheckIn");
        Assert.True(proposed.Succeeded);
        Assert.Equal((AttendanceCorrectionStatus.Pending, AttendanceExceptionStatus.PendingCorrection), (proposed.Status, proposed.ExceptionStatus));

        var decided = await DecideAsync(proposed.CorrectionId!.Value, _managerId, approved: true);

        Assert.Equal((AttendanceCorrectionStatus.Applied, AttendanceExceptionStatus.Closed), (decided.Status, decided.ExceptionStatus));
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var correction = Assert.Single(db.AttendanceCorrections);
        Assert.Equal(
            (AttendanceCorrectionKind.ChangePunchType, NormalizedEventType.CheckOut, NormalizedEventType.CheckIn, Monday.At(13, 0)),
            (correction.Kind, correction.OriginalEventType, correction.CorrectedEventType, correction.ProposedValue));
        Assert.NotNull(correction.NormalizedTransactionId);

        // The normalized punch is untouched (CLAUDE.md §1.7): only the engine reads it as a check-in.
        var punch = db.NormalizedAttendanceTransactions.Single(n => n.Id == correction.NormalizedTransactionId);
        Assert.Equal((NormalizedEventType.CheckOut, Monday.At(13, 0)), (punch.NormalizedEventType, punch.EventTimestampLocal));

        var record = db.AttendanceRecords.Single(r => r.IsCurrent);
        Assert.Equal(correction.Id.ToString(), record.SourceCorrectionIds);
        Assert.Equal(TimeSpan.FromHours(4), record.WorkingHours);
        Assert.DoesNotContain(db.AttendanceExceptions.ToList(), e => e.ExceptionType == AttendanceExceptionType.InvalidSequence && e.Status != AttendanceExceptionStatus.Closed);
        Assert.Equal(AttendanceExceptionResolutionMethod.Correction, db.AttendanceExceptions.Single(e => e.Id == exceptionId).ResolutionMethod);
    }

    [Fact]
    public async Task AnAppliedChange_SurvivesReprocessing_AndWhatItLeavesUndoneIsAnotherException()
    {
        AddPunches("I@08:00,O@12:00,O@13:00");
        await ProcessAsync(Monday);
        var proposed = await ProposeAsync(InvalidSequence().Id, "CheckIn");
        await DecideAsync(proposed.CorrectionId!.Value, _managerId, approved: true);

        // The shift is over and the employee is checked in with no check-out: a missing punch, as for any day.
        Clock.Local = Monday.AddDays(1);
        await ProcessAsync(Monday);
        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var open = db.AttendanceExceptions.Where(e => e.Status != AttendanceExceptionStatus.Closed).ToList();
        Assert.Equal(NormalizedEventType.CheckOut, Assert.Single(open).MissingEventType);
        Assert.Equal(AttendanceExceptionType.MissingPunch, open.Single().ExceptionType);
    }

    [Fact]
    public async Task ARejectedChange_ReopensTheException_AndTheDayIsUnchanged()
    {
        AddPunches("I@08:00,O@12:00,O@13:00");
        await ProcessAsync(Monday);
        var exceptionId = InvalidSequence().Id;
        var proposed = await ProposeAsync(exceptionId, "CheckIn");

        var rejected = await DecideAsync(proposed.CorrectionId!.Value, _managerId, approved: false);

        Assert.Equal((AttendanceCorrectionStatus.Rejected, AttendanceExceptionStatus.Open), (rejected.Status, rejected.ExceptionStatus));
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(1, db.AttendanceRecords.Count());
        Assert.Equal(AttendanceExceptionStatus.Open, db.AttendanceExceptions.Single(e => e.Id == exceptionId).Status);

        // And a new proposal is possible again.
        Assert.True((await ProposeAsync(exceptionId, "CheckIn")).Succeeded);
    }

    [Fact]
    public async Task OnlyATypeTheExceptionOffers_OnlyByItsOwnEmployee_WithAJustification_IsProposed()
    {
        AddPunches("I@08:00,O@12:00,O@13:00");
        await ProcessAsync(Monday);
        var exceptionId = InvalidSequence().Id;

        var notOffered = await ProposeAsync(exceptionId, "BreakOut");
        var notAType = await ProposeAsync(exceptionId, "Lunch");
        var byManager = await ProposeAsync(exceptionId, "CheckIn", requestedBy: _managerId);
        var noReason = await ProposeAsync(exceptionId, "CheckIn", justification: "  ");

        Assert.Contains("Choose one of: CheckIn", notOffered.Errors[nameof(ProposeCorrectionRequest.NewEventType)]);
        Assert.Contains(nameof(ProposeCorrectionRequest.NewEventType), notAType.Errors.Keys);
        Assert.Contains(CorrectionErrorKeys.RequestedBy, byManager.Errors.Keys);
        Assert.Contains(nameof(ProposeCorrectionRequest.Justification), noReason.Errors.Keys);
        Assert.True((await ProposeAsync(999, "CheckIn")).NotFound);

        // None of the refusals stored anything, and the exception is still open.
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceCorrections);
        Assert.Equal(AttendanceExceptionStatus.Open, db.AttendanceExceptions.Single(e => e.Id == exceptionId).Status);
    }

    [Fact]
    public async Task AMissingPunchHasNoTypeToChange_AndAnOutOfOrderPunchHasNoTimeToPropose()
    {
        AddPunches("I@08:00,O@12:00,O@13:00");
        Clock.Local = Monday.At(23, 0);
        await ProcessAsync(Monday);

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            // A day that also lacks something is a different exception: the shift has no break, so there is no missing break punch here;
            // the check-in is missing on another day.
            Assert.Single(db.AttendanceExceptions);
        }

        var asTime = await ProposeTimeAsync(InvalidSequence().Id, Monday.At(13, 0));

        Assert.Contains("Only a missing punch", asTime.Errors[nameof(ProposeCorrectionRequest.ExceptionId)]);
    }

    [Fact]
    public async Task ProposingAChangeReadsTheNormalizedPunch_SoItIsAudited()
    {
        AddPunches("I@08:00,O@12:00,O@13:00");
        await ProcessAsync(Monday);

        await ProposeAsync(InvalidSequence().Id, "CheckIn");

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Contains(db.AttendanceAuditLogs.ToList(), a => a.Action == AttendanceAuditActions.AttendanceDataRead && a.TargetType == nameof(NormalizedAttendanceTransaction));
        Assert.Contains(db.AttendanceAuditLogs.ToList(), a => a.Action == AttendanceAuditActions.ExceptionStatusChanged && a.Details!.Contains("from CheckOut to CheckIn"));
    }

    [Fact]
    public async Task TheExceptionsListOffersTheChange_ToTheEmployee_NotToTheirManager()
    {
        AddPunches("I@08:00,O@12:00,O@13:00");
        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var query = new AttendanceExceptionQuery(db);
        var asEmployee = Assert.Single(await query.ListAsync(new ExceptionFilter(ExceptionStatusFilter.Active, null, null, null, 100), new ExceptionViewer(EmployeeId, true, false, false), CancellationToken.None));
        var asManager = Assert.Single(await query.ListAsync(new ExceptionFilter(ExceptionStatusFilter.Active, null, null, null, 100), new ExceptionViewer(_managerId, false, true, false), CancellationToken.None));

        Assert.True(asEmployee.CanPropose);
        Assert.False(asManager.CanPropose);
        Assert.Equal(["CheckIn"], asEmployee.SuggestedEventTypes);
    }

    [Fact]
    public async Task TheApproversQueueSaysWhatIsChanged()
    {
        AddPunches("I@08:00,O@12:00,O@13:00");
        await ProcessAsync(Monday);
        await ProposeAsync(InvalidSequence().Id, "CheckIn");

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var item = Assert.Single(await CreateService(db).ListPendingAsync(CorrectionApprover.Manager, _managerId, CancellationToken.None));

        Assert.Equal(("ChangePunchType", "CheckOut", "CheckIn", Monday.At(13, 0)), (item.Kind, item.OriginalEventType, item.CorrectedEventType, item.ProposedValue));
    }

    private AttendanceException InvalidSequence()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return db.AttendanceExceptions.Single(e => e.ExceptionType == AttendanceExceptionType.InvalidSequence);
    }

    private async Task<CorrectionResult> ProposeAsync(int exceptionId, string newType, int? requestedBy = null, string justification = "I came back from lunch.")
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await CreateService(db).ProposeTypeChangeAsync(exceptionId, newType, justification, requestedBy ?? EmployeeId, CancellationToken.None);
    }

    private async Task<CorrectionResult> ProposeTimeAsync(int exceptionId, DateTime proposed)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await CreateService(db).ProposeCorrectionAsync(exceptionId, proposed, "Because.", EmployeeId, CancellationToken.None);
    }

    private async Task<CorrectionResult> DecideAsync(int correctionId, int decidedBy, bool approved)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await CreateService(db).DecideCorrectionAsync(correctionId, decidedBy, approved, false, CancellationToken.None);
    }

    private AttendanceCorrectionService CreateService(RobeemDbContext db) =>
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
