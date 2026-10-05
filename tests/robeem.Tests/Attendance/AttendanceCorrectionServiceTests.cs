using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// The correction workflow (study §11). Scenario 3 is the one of the exception tests: the
/// break-out is missing and is corrected to 12:00 (reconstructed inputs, see ATTENDANCE_P9_NOTES.md).
/// </summary>
public sealed class AttendanceCorrectionServiceTests : AttendanceEngineTestBase
{
    private readonly int _managerId;
    private readonly int _hrId;

    public AttendanceCorrectionServiceTests()
    {
        _managerId = AddProfile("M-1", "Grace", "Hopper");
        _hrId = AddProfile("H-1", "Alan", "Turing");
        Clock.Local = Monday.At(18, 0);
    }

    [Fact]
    public async Task Scenario3_ProposeThenManagerApproves_RecalculatesTheDay_AndClosesTheException()
    {
        var exceptionId = await GivenMissingBreakOutAsync();

        var proposed = await ProposeAsync(exceptionId, Monday.At(12, 0));
        Assert.True(proposed.Succeeded);
        Assert.Equal((AttendanceCorrectionStatus.Pending, AttendanceExceptionStatus.PendingCorrection), (proposed.Status, proposed.ExceptionStatus));

        var decided = await DecideAsync(proposed.CorrectionId!.Value, _managerId, approved: true, isHr: false);

        Assert.Equal((AttendanceCorrectionStatus.Applied, AttendanceExceptionStatus.Closed), (decided.Status, decided.ExceptionStatus));
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var correction = Assert.Single(db.AttendanceCorrections);
        Assert.Equal((NormalizedEventType.BreakOut, Monday.At(12, 0), AttendanceCorrectionStatus.Applied), (correction.CorrectedEventType, correction.ProposedValue, correction.Status));
        Assert.Equal((EmployeeId, "Because the reader missed it."), (correction.RequestedByEmployeeId, correction.Justification));
        Assert.Equal((AttendanceCorrectionDecision.Approved, _managerId), (correction.ManagerDecision, correction.ManagerDecidedById));
        Assert.Null(correction.HrDecision);
        Assert.Equal(Monday.At(18, 0), correction.AppliedAt);

        var exception = Assert.Single(db.AttendanceExceptions);
        Assert.Equal((AttendanceExceptionStatus.Closed, AttendanceExceptionResolutionMethod.Correction), (exception.Status, exception.ResolutionMethod));
        Assert.Equal(Monday.At(18, 0), exception.ResolvedAt);

        // The day is recalculated into version 2 with the corrected punch; version 1 is kept.
        var versions = db.AttendanceRecords.OrderBy(r => r.Version).ToList();
        Assert.Equal(2, versions.Count);
        Assert.Equal((false, AttendanceRecordStatus.PendingReview, TimeSpan.FromHours(9)), (versions[0].IsCurrent, versions[0].Status, versions[0].WorkingHours));
        var record = versions[1];
        Assert.True(record.IsCurrent);
        Assert.Equal(Monday.At(12, 0), record.ActualBreakOut);
        Assert.Equal(AttendancePunchSource.Corrected, record.ActualBreakOutSource);
        Assert.Equal(AttendancePunchSource.Normalized, record.ActualBreakInSource);
        Assert.Equal((TimeSpan.FromHours(8), TimeSpan.FromHours(1)), (record.WorkingHours, record.BreakDuration));
        Assert.Equal(correction.Id.ToString(), record.SourceCorrectionIds);
        Assert.Equal(AttendanceRecordStatus.Approved, record.Status);

        var summary = Assert.Single(db.AttendanceDaySummaries);
        Assert.Equal((0, AttendanceRecordStatus.Approved), (summary.ExceptionCount, summary.Status));

        // The normalized punches are untouched: the correction is additive (CLAUDE.md §1.7).
        Assert.Equal(3, db.NormalizedAttendanceTransactions.Count());
    }

    [Fact]
    public async Task AnAppliedCorrection_SurvivesReprocessing()
    {
        var exceptionId = await GivenMissingBreakOutAsync();
        var proposed = await ProposeAsync(exceptionId, Monday.At(12, 0));
        await DecideAsync(proposed.CorrectionId!.Value, _managerId, approved: true, isHr: false);

        var again = await ProcessAsync(Monday);

        Assert.Equal(new DayProcessingResult(0, 1, 0), again);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceRecordStatus.Approved, db.AttendanceRecords.Single(r => r.IsCurrent).Status);
        Assert.Equal(AttendanceExceptionStatus.Closed, Assert.Single(db.AttendanceExceptions).Status);
    }

    [Fact]
    public async Task ProposeThenManagerRejects_ReopensTheException_AndAllowsANewProposal()
    {
        var exceptionId = await GivenMissingBreakOutAsync();
        var first = await ProposeAsync(exceptionId, Monday.At(12, 0));

        var rejected = await DecideAsync(first.CorrectionId!.Value, _managerId, approved: false, isHr: false);

        Assert.Equal((AttendanceCorrectionStatus.Rejected, AttendanceExceptionStatus.Open), (rejected.Status, rejected.ExceptionStatus));
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var correction = Assert.Single(db.AttendanceCorrections);
            Assert.Equal((AttendanceCorrectionDecision.Rejected, _managerId), (correction.ManagerDecision, correction.ManagerDecidedById));
            Assert.Equal(AttendanceExceptionStatus.Open, Assert.Single(db.AttendanceExceptions).Status);

            // Nothing was recalculated: still the one record, still waiting for review.
            var record = Assert.Single(db.AttendanceRecords);
            Assert.Equal((1, AttendanceRecordStatus.PendingReview), (record.Version, record.Status));
        }

        var second = await ProposeAsync(exceptionId, Monday.At(12, 5));
        Assert.True(second.Succeeded);
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(2, check.AttendanceCorrections.Count());
        Assert.Equal(AttendanceExceptionStatus.PendingCorrection, Assert.Single(check.AttendanceExceptions).Status);
    }

    [Fact]
    public async Task WhenThePolicyAsksForManagerAndHr_OnlyTheManagersApproval_LeavesTheCorrectionPending()
    {
        AddDefaultPolicy(p => p.CorrectionApprovalWorkflow = CorrectionApprovalWorkflow.Both);
        var exceptionId = await GivenMissingBreakOutAsync();
        var proposed = await ProposeAsync(exceptionId, Monday.At(12, 0));

        var afterManager = await DecideAsync(proposed.CorrectionId!.Value, _managerId, approved: true, isHr: false);

        Assert.True(afterManager.Succeeded);
        Assert.Equal((AttendanceCorrectionStatus.Pending, AttendanceExceptionStatus.PendingCorrection), (afterManager.Status, afterManager.ExceptionStatus));
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var pending = Assert.Single(db.AttendanceCorrections);
            Assert.Equal((AttendanceCorrectionStatus.Pending, AttendanceCorrectionDecision.Approved), (pending.Status, pending.ManagerDecision));
            Assert.Null(pending.HrDecision);
            Assert.Null(pending.AppliedAt);

            // Not applied, and the day is untouched: still the first version, still not approved.
            var record = Assert.Single(db.AttendanceRecords);
            Assert.Equal((1, AttendanceRecordStatus.PendingReview), (record.Version, record.Status));
            Assert.Null(record.ActualBreakOut);
        }

        // The correction now waits in HR's queue, and has left the manager's.
        Assert.Empty(await ListPendingAsync(CorrectionApprover.Manager));
        Assert.Equal(proposed.CorrectionId, Assert.Single(await ListPendingAsync(CorrectionApprover.HR)).Id);

        var afterHr = await DecideAsync(proposed.CorrectionId!.Value, _hrId, approved: true, isHr: true);

        Assert.Equal((AttendanceCorrectionStatus.Applied, AttendanceExceptionStatus.Closed), (afterHr.Status, afterHr.ExceptionStatus));
        using var check = Database.CreateContext(TestDatabase.TenantA);
        var applied = Assert.Single(check.AttendanceCorrections);
        Assert.Equal((AttendanceCorrectionDecision.Approved, _hrId), (applied.HrDecision, applied.HrDecidedById));
        Assert.Equal(AttendanceRecordStatus.Approved, check.AttendanceRecords.Single(r => r.IsCurrent).Status);
    }

    [Fact]
    public async Task WhenThePolicyAsksForManagerAndHr_HrCannotDecideFirst_AndTheManagersRejectionEndsIt()
    {
        AddDefaultPolicy(p => p.CorrectionApprovalWorkflow = CorrectionApprovalWorkflow.Both);
        var exceptionId = await GivenMissingBreakOutAsync();
        var proposed = await ProposeAsync(exceptionId, Monday.At(12, 0));
        var id = proposed.CorrectionId!.Value;

        var early = await DecideAsync(id, _hrId, approved: true, isHr: true);
        Assert.Contains(nameof(DecideCorrectionRequest.IsHrDecision), early.Errors.Keys);

        var rejected = await DecideAsync(id, _managerId, approved: false, isHr: false);
        Assert.Equal((AttendanceCorrectionStatus.Rejected, AttendanceExceptionStatus.Open), (rejected.Status, rejected.ExceptionStatus));
    }

    [Fact]
    public async Task WhenThePolicyAsksForHrOnly_TheManagerCannotDecide_AndHrAlone_Applies()
    {
        AddDefaultPolicy(p => p.CorrectionApprovalWorkflow = CorrectionApprovalWorkflow.HR);
        var exceptionId = await GivenMissingBreakOutAsync();
        var id = (await ProposeAsync(exceptionId, Monday.At(12, 0))).CorrectionId!.Value;

        Assert.Empty(await ListPendingAsync(CorrectionApprover.Manager));
        Assert.Contains(nameof(DecideCorrectionRequest.IsHrDecision), (await DecideAsync(id, _managerId, approved: true, isHr: false)).Errors.Keys);

        Assert.Equal(AttendanceCorrectionStatus.Applied, (await DecideAsync(id, _hrId, approved: true, isHr: true)).Status);
    }

    [Fact]
    public async Task WhenThePolicyAsksForTheManagerOnly_AnHrDecisionIsRefused()
    {
        var exceptionId = await GivenMissingBreakOutAsync();
        var id = (await ProposeAsync(exceptionId, Monday.At(12, 0))).CorrectionId!.Value;

        var result = await DecideAsync(id, _hrId, approved: true, isHr: true);

        Assert.Contains(nameof(DecideCorrectionRequest.IsHrDecision), result.Errors.Keys);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceCorrectionStatus.Pending, Assert.Single(db.AttendanceCorrections).Status);
    }

    [Fact]
    public async Task ThePendingList_ShowsWhatTheApproverIsAskedToDecide()
    {
        var exceptionId = await GivenMissingBreakOutAsync();
        var proposed = await ProposeAsync(exceptionId, Monday.At(12, 0));

        var item = Assert.Single(await ListPendingAsync(CorrectionApprover.Manager));

        Assert.Equal(proposed.CorrectionId, item.Id);
        Assert.Equal((exceptionId, EmployeeId, "Ada Lovelace"), (item.ExceptionId, item.EmployeeId, item.EmployeeName));
        Assert.Equal((Monday, "BreakOut", Monday.At(12, 0)), (item.AttendanceDate, item.CorrectedEventType, item.ProposedValue));
        Assert.Equal(("Manager", "Because the reader missed it.", null), (item.ApprovalWorkflow, item.Justification, item.ManagerDecision));
        Assert.Empty(await ListPendingAsync(CorrectionApprover.HR));
    }

    [Fact]
    public async Task ProposingIsRefused_WhenTheExceptionIsNotOpenAndMissingAPunch_OrNotTheRequestersOwn()
    {
        var exceptionId = await GivenMissingBreakOutAsync();
        var other = AddProfile("E-2", "Charles", "Babbage");

        Assert.Contains(CorrectionErrorKeys.RequestedBy, (await ProposeAsync(exceptionId, Monday.At(12, 0), requestedBy: other)).Errors.Keys);
        Assert.Contains(nameof(ProposeCorrectionRequest.Justification), (await ProposeAsync(exceptionId, Monday.At(12, 0), justification: "  ")).Errors.Keys);
        Assert.Contains(nameof(ProposeCorrectionRequest.ProposedValue), (await ProposeAsync(exceptionId, Monday.At(18, 30))).Errors.Keys);
        Assert.Contains(nameof(ProposeCorrectionRequest.ProposedValue), (await ProposeAsync(exceptionId, Monday.AddDays(-1).At(12, 0))).Errors.Keys);
        Assert.True((await ProposeAsync(999, Monday.At(12, 0))).NotFound);

        // Nothing was stored by the refusals, and the exception is still open.
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Empty(db.AttendanceCorrections);
            Assert.Equal(AttendanceExceptionStatus.Open, Assert.Single(db.AttendanceExceptions).Status);
        }

        // One at a time: a second proposal waits for the first one's decision.
        Assert.True((await ProposeAsync(exceptionId, Monday.At(12, 0))).Succeeded);
        Assert.Contains(nameof(ProposeCorrectionRequest.ExceptionId), (await ProposeAsync(exceptionId, Monday.At(12, 5))).Errors.Keys);
    }

    [Fact]
    public async Task AnInvalidSequence_CannotBeCorrectedByAddingAPunch()
    {
        AssignSchedule(StandardDay);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 10));
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));
        await ProcessAsync(Monday);
        int exceptionId;
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            exceptionId = Assert.Single(db.AttendanceExceptions).Id;
        }

        var result = await ProposeAsync(exceptionId, Monday.At(8, 5));

        Assert.Contains(nameof(ProposeCorrectionRequest.ExceptionId), result.Errors.Keys);
    }

    [Fact]
    public async Task DecidingIsRefused_ForTheProposer_AnUnknownUser_OrACorrectionAlreadyDecided()
    {
        var exceptionId = await GivenMissingBreakOutAsync();
        var id = (await ProposeAsync(exceptionId, Monday.At(12, 0))).CorrectionId!.Value;

        Assert.Contains(CorrectionErrorKeys.DecidedBy, (await DecideAsync(id, EmployeeId, approved: true, isHr: false)).Errors.Keys);
        Assert.Contains(CorrectionErrorKeys.DecidedBy, (await DecideAsync(id, 999, approved: true, isHr: false)).Errors.Keys);
        Assert.True((await DecideAsync(999, _managerId, approved: true, isHr: false)).NotFound);

        await DecideAsync(id, _managerId, approved: true, isHr: false);
        var again = await DecideAsync(id, _managerId, approved: false, isHr: false);
        Assert.False(again.Succeeded);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceCorrectionStatus.Applied, Assert.Single(db.AttendanceCorrections).Status);
    }

    [Fact]
    public async Task ADecision_WritesAnAuditEntry_WithTheDecidersActor()
    {
        var exceptionId = await GivenMissingBreakOutAsync();
        var correctionId = (await ProposeAsync(exceptionId, Monday.At(12, 0))).CorrectionId!.Value;

        await DecideAsync(correctionId, _managerId, approved: true, isHr: false);

        var decision = Assert.Single(await AuditAsync(AttendanceAuditActions.CorrectionDecided));
        Assert.Equal($"employee:{_managerId}", decision.Actor);
        Assert.Equal((nameof(AttendanceCorrection), correctionId), (decision.EntityType, decision.EntityId));
        Assert.Contains("Approved by Manager", decision.Details);
        Assert.Contains("Applied", decision.Details);
    }

    [Fact]
    public async Task EveryExceptionStatusChange_IsAudited_WithItsActor()
    {
        var exceptionId = await GivenMissingBreakOutAsync();
        var correctionId = (await ProposeAsync(exceptionId, Monday.At(12, 0))).CorrectionId!.Value;
        await DecideAsync(correctionId, _managerId, approved: true, isHr: false);

        var changes = (await AuditAsync(AttendanceAuditActions.ExceptionStatusChanged)).OrderBy(e => e.Id).ToList();

        // Raised by the engine (the system), proposed by the employee, closed by the manager's approval.
        Assert.Equal(["system", $"employee:{EmployeeId}", $"employee:{_managerId}"], changes.Select(c => c.Actor));
        Assert.All(changes, c => Assert.Equal((nameof(AttendanceException), exceptionId), (c.EntityType, c.EntityId)));
        Assert.StartsWith("(new) -> Open", changes[0].Details);
        Assert.StartsWith("Open -> PendingCorrection", changes[1].Details);
        Assert.StartsWith("PendingCorrection -> Closed", changes[2].Details);
    }

    [Fact]
    public async Task ARejection_IsAuditedAsADecision_AndAsTheExceptionReopening()
    {
        var exceptionId = await GivenMissingBreakOutAsync();
        var correctionId = (await ProposeAsync(exceptionId, Monday.At(12, 0))).CorrectionId!.Value;

        await DecideAsync(correctionId, _managerId, approved: false, isHr: false);

        var decision = Assert.Single(await AuditAsync(AttendanceAuditActions.CorrectionDecided));
        Assert.Contains("Rejected by Manager", decision.Details);
        Assert.Equal($"employee:{_managerId}", decision.Actor);
        var reopened = (await AuditAsync(AttendanceAuditActions.ExceptionStatusChanged)).OrderBy(e => e.Id).Last();
        Assert.StartsWith("PendingCorrection -> Open", reopened.Details);
        Assert.Equal($"employee:{_managerId}", reopened.Actor);
    }

    [Fact]
    public async Task ARefusedDecision_WritesNoDecisionEntry()
    {
        var exceptionId = await GivenMissingBreakOutAsync();
        var correctionId = (await ProposeAsync(exceptionId, Monday.At(12, 0))).CorrectionId!.Value;

        await DecideAsync(correctionId, EmployeeId, approved: true, isHr: false);

        Assert.Empty(await AuditAsync(AttendanceAuditActions.CorrectionDecided));
    }

    [Fact]
    public async Task AStepOfABothWorkflow_IsAudited_WhileTheCorrectionIsStillPending()
    {
        AddDefaultPolicy(p => p.CorrectionApprovalWorkflow = CorrectionApprovalWorkflow.Both);
        var exceptionId = await GivenMissingBreakOutAsync();
        var correctionId = (await ProposeAsync(exceptionId, Monday.At(12, 0))).CorrectionId!.Value;

        await DecideAsync(correctionId, _managerId, approved: true, isHr: false);
        await DecideAsync(correctionId, _hrId, approved: true, isHr: true);

        var decisions = (await AuditAsync(AttendanceAuditActions.CorrectionDecided)).OrderBy(e => e.Id).ToList();
        Assert.Equal([$"employee:{_managerId}", $"employee:{_hrId}"], decisions.Select(d => d.Actor));
        Assert.Contains("still Pending", decisions[0].Details);
        Assert.Contains("Approved by HR; the correction is Applied", decisions[1].Details);
    }

    [Fact]
    public async Task ProposingReadsNormalizedAttendance_AndThatReadIsAudited()
    {
        var exceptionId = await GivenMissingBreakOutAsync();

        await ProposeAsync(exceptionId, Monday.At(12, 0));

        var read = Assert.Single(await AuditAsync(AttendanceAuditActions.AttendanceDataRead));
        Assert.Equal($"employee:{EmployeeId}", read.Actor);
        Assert.Equal(nameof(NormalizedAttendanceTransaction), read.EntityType);
    }

    [Fact]
    public async Task AProposalRefusedForItsTimeOrJustification_StillAuditsTheRead()
    {
        var exceptionId = await GivenMissingBreakOutAsync();

        Assert.False((await ProposeAsync(exceptionId, Monday.At(12, 0), justification: " ")).Succeeded);

        // The read happened and is recorded; the exception did not move.
        Assert.Single(await AuditAsync(AttendanceAuditActions.AttendanceDataRead));
        Assert.DoesNotContain(await AuditAsync(AttendanceAuditActions.ExceptionStatusChanged), e => e.Details!.Contains("PendingCorrection"));
    }

    private async Task<IReadOnlyList<AuditLogEntry>> AuditAsync(string action)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await CreateAudit(db).ListAsync(action, null, null, 100, CancellationToken.None);
    }

    private async Task<int> GivenMissingBreakOutAsync()
    {
        AssignSchedule(StandardDay);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));
        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var exception = Assert.Single(db.AttendanceExceptions);
        Assert.Equal(NormalizedEventType.BreakOut, exception.MissingEventType);
        return exception.Id;
    }

    private int AddProfile(string number, string firstName, string lastName)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var profile = new Profile { EmployeeNumber = number, FirstName = firstName, LastName = lastName, Title = $"{firstName} {lastName}" };
        db.Add(profile);
        db.SaveChanges();
        return profile.Id;
    }

    private async Task<CorrectionResult> ProposeAsync(int exceptionId, DateTime proposed, int? requestedBy = null, string justification = "Because the reader missed it.")
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await CreateService(db).ProposeCorrectionAsync(exceptionId, proposed, justification, requestedBy ?? EmployeeId, CancellationToken.None);
    }

    private async Task<CorrectionResult> DecideAsync(int correctionId, int decidedBy, bool approved, bool isHr)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await CreateService(db).DecideCorrectionAsync(correctionId, decidedBy, approved, isHr, CancellationToken.None);
    }

    private async Task<IReadOnlyList<CorrectionListItem>> ListPendingAsync(CorrectionApprover approver)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await CreateService(db).ListPendingAsync(approver, CancellationToken.None);
    }

    private AttendanceCorrectionService CreateService(RobeemDbContext db) =>
        new(db, new AttendancePolicyResolver(db), CreateEngine(db), CreateAudit(db), Clock);
}
